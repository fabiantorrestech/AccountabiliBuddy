using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The flexible lunch (schema v8, design part B): on any workday "Start lunch" (on any of the user's devices) starts it
/// for the lunch length (Settings → Your day, 1 hour by default); while it runs, +5 / −5 min change its end. A flexible
/// break in today's plan is only a guideline: its window is drawn on the day bar and gives the hint time. A running
/// session steps away as a Break for it, held while lunch runs (input doesn't end it; "I'm back" or the lunch ending
/// does, <see cref="StepAwayService.HoldsAway"/>). Not started by its hint time: one hint; never started: skipped (the window
/// stays work). A fixed lunch period (schema v13, a Rest part marked to start by itself) starts by itself on a
/// workday at the first moment within its window (part start to part end) when no session runs (so it isn't a test, and a session
/// that was kept has ended) and the user is at the PC (input in the last 2 minutes, else it waits for the first input; also when the
/// app starts inside the window), as if Start lunch was pressed. It
/// always ends where the part does, so a late start is the rest of the window. Not when lunch was taken or the user chose
/// "Flexible this time" (<see cref="LunchRunIntoService"/>). When it's over: one chime and "Lunch over!". A lunch started or changed on another device arrives in
/// its sync file (<see cref="SyncFile.Lunch"/>) and is taken here when its edit is newer. Document "lunch". Use from
/// the UI thread only.
/// </summary>
public sealed class LunchService
{
    private const string DocumentKey = "lunch";

    /// <summary>The lunch length setting's range and step, in minutes.</summary>
    public const int MinLengthMinutes = 15;
    public const int MaxLengthMinutes = 120;
    public const int LengthStepMinutes = 5;

    /// <summary>
    /// The longest a whole lunch (start to end) can be, whatever +5 min or another device says: lunch holds a session's
    /// step away, so it must never hold it for long.
    /// </summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(MaxLengthMinutes);

    /// <summary>How much +5 / −5 min changes a running lunch by.</summary>
    public static readonly TimeSpan AdjustStep = TimeSpan.FromMinutes(5);

    /// <summary>A shortened lunch ends no sooner than this after now.</summary>
    private static readonly TimeSpan ShortestLeft = TimeSpan.FromMinutes(1);

    /// <summary>A fixed lunch starts by itself only with input this recently: the user is at the PC.</summary>
    private static readonly TimeSpan PresenceWindow = TimeSpan.FromMinutes(2);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Action<string, string> _remind;
    private readonly Action _chime;
    private readonly IUserIdleDetector _idle;

    // The last lunch record received from each device, as it came (before any clamp); in memory only, so a restart reads each file's lunch once more.
    private readonly Dictionary<Guid, SyncLunch> _lastReceived = [];
    private LunchState _state = new();

    /// <summary>True while another of the user's PCs has a live session (its received file says so); set by the app. A fixed lunch doesn't start by itself then.</summary>
    public Func<bool>? OtherPcSessionRunning { get; set; }

    public LunchService(
        SessionController sessions, ConfigurationService configuration, SqliteJsonDocumentStore store, Action<string, string> remind, Action chime,
        IUserIdleDetector idle)
    {
        _sessions = sessions;
        _configuration = configuration;
        _store = store;
        _remind = remind;
        _chime = chime;
        _idle = idle;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    /// <summary>Raised when lunch starts, changes or ends, and when the length setting changes (the Today card, the day bar and the sync file follow).</summary>
    public event EventHandler? Changed;

    /// <summary>Today's flexible break from the plan (the usual lunch window and the hint); null on a day without one.</summary>
    public FlexibleBreak? Today => _configuration.Current.DayPlan.FlexibleBreakOn(DateOnly.FromDateTime(LocalNow()));

    /// <summary>
    /// True on a workday, by the plan's one rule (<see cref="DayPlan.IsWorkday"/>, the phone's too): its week map (A or B)
    /// gives the day a workday template, or a one-off day has work in it. A one-off day replaces a workday's template, so
    /// a one-off day off on a Monday isn't one.
    /// </summary>
    public bool IsWorkdayToday => _configuration.Current.DayPlan.IsWorkday(DateOnly.FromDateTime(LocalNow()));

    /// <summary>How long a lunch lasts when it starts (the setting; it wins over a flexible break's own length).</summary>
    public TimeSpan Length => TimeSpan.FromMinutes(Math.Clamp(_state.LengthMinutes, MinLengthMinutes, MaxLengthMinutes));

    /// <summary>Today's lunch once started (it may be over); null before it starts.</summary>
    public SyncLunch? Taken => _state.Start is DateTimeOffset start && _state.End is DateTimeOffset end && IsToday(start)
        ? new SyncLunch(start, end, _state.EditedAt, _state.AllowanceMinutes, _state.Counted, _state.Discarded)
        : null;

    /// <summary>True while lunch runs.</summary>
    public bool IsOnLunch => Taken is SyncLunch lunch && _sessions.Now < lunch.End;

    /// <summary>
    /// True while lunch holds a session's step away (input doesn't end it): during lunch, and never past the day's lunch total
    /// (<see cref="MaxLength"/> counting the earlier pieces too, <see cref="LunchDay.Held"/>), even if the stored end says otherwise.
    /// </summary>
    public bool HoldsSessionAway => IsOnLunch && Taken is SyncLunch lunch && _sessions.Now < LunchDay.Held(lunch).End;

    /// <summary>
    /// Whether the active mode lets "Discard it" through (Settings › Tasks › "Allow discarding a lunch", per mode, ADR 88); set by the app.
    /// Until the app sets it, discarding is off: nothing is discarded by accident.
    /// </summary>
    public Func<bool> DiscardAllowed { get; set; } = () => false;

    /// <summary>
    /// Wires <see cref="DiscardAllowed"/> to the active mode's "Allow discarding a lunch" (Settings › Tasks; the app calls it once). The service is read
    /// on every ask, so a mode switch mid-lunch takes the other mode's copy at once; none yet (the app is starting): off.
    /// </summary>
    public void FollowDiscardSetting(Func<TaskSettingsService?> settings) => DiscardAllowed = () => settings()?.Current.AllowLunchDiscard ?? false;

    /// <summary>"Discard it" is offered now: lunch runs and the mode allows it.</summary>
    public bool CanDiscard => Taken is SyncLunch lunch && LunchDay.CanDiscard(lunch, _sessions.Now, DiscardAllowed());

    /// <summary>The lunch used so far today (counted pieces plus the running one); zero before a lunch.</summary>
    public TimeSpan Used => Taken is SyncLunch lunch ? LunchDay.Used(lunch, _sessions.Now) : TimeSpan.Zero;

    /// <summary>What is left of today's lunch allowance (the whole length before a lunch), never negative.</summary>
    public TimeSpan Remaining => Taken is SyncLunch lunch ? LunchDay.Remaining(lunch, _sessions.Now, (int)Length.TotalMinutes) : Length;

    /// <summary>Another piece of today's lunch can start: nothing runs and an earlier piece was counted or discarded with time left (the rest, ADR 88).</summary>
    public bool CanStartRest => Taken is SyncLunch lunch && LunchDay.CanStart(lunch, _sessions.Now, (int)Length.TotalMinutes);

    /// <summary>Today's fixed lunch period from the plan (schema v13); null on a day without one.</summary>
    public FixedLunch? FixedToday => _configuration.Current.DayPlan.FixedLunchOn(DateOnly.FromDateTime(LocalNow()));

    /// <summary>True when "Flexible this time" was chosen today: the fixed lunch is left to the user's Start lunch.</summary>
    public bool IsFlexibleToday => _state.NoAutoStartOn == DateOnly.FromDateTime(LocalNow());

    /// <summary>
    /// Today's fixed lunch is still to start by itself: a workday with one, no lunch taken yet and "Flexible this time" not chosen.
    /// </summary>
    /// <remarks>Only until its window ends: after that it can no longer start by itself, and "Start lunch" is the user's.</remarks>
    public FixedLunch? AutoStartPending => IsWorkdayToday && LunchDay.AutoStartPending(Taken) && !IsFlexibleToday && FixedToday is FixedLunch lunch && LocalNow() < lunch.Window.End
        ? lunch
        : null;

    /// <summary>"Start lunch" shows: today is a workday (or has a flexible break) and no lunch was taken yet.</summary>
    public bool CanStart => (IsWorkdayToday || Today is not null) && Taken is null;

    /// <summary>"Start the rest" shows: an earlier piece was counted or discarded today and time is left. (A lunch was started, so the day needs no check.)</summary>
    public bool CanStartTheRest => CanStartRest;

    /// <summary>−5 min can still take something off: the running lunch ends more than a minute from now.</summary>
    public bool CanShorten => IsOnLunch && Taken is SyncLunch lunch && lunch.End > _sessions.Now + ShortestLeft;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<LunchState>(DocumentKey)).GetAwaiter().GetResult() ?? new LunchState();
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = new LunchState();
        }
    }

    /// <summary>
    /// Starts lunch now for the lunch length, or, when an earlier piece today was counted or discarded, for what is left of the day's
    /// allowance (ADR 88); a running session steps away.
    /// </summary>
    public Task StartAsync() => Taken is null ? StartForAsync(Length) : StartRestAsync();

    private async Task StartRestAsync()
    {
        DateTimeOffset now = _sessions.Now;
        // Only today's lunch is passed (Taken is today's), as LunchDay.StartAgain needs.
        if (Taken is not SyncLunch lunch || LunchDay.StartAgain(lunch, now, (int)Length.TotalMinutes) is not SyncLunch next)
        {
            return;
        }

        await SaveAsync(_state with
        {
            Start = next.Start, End = next.End, OverTold = false, EditedAt = next.EditedAt, AllowanceMinutes = next.Allowance, Counted = next.Counted, Discarded = false,
        });
        StepAwayForLunch();
    }

    private async Task StartForAsync(TimeSpan length)
    {
        DateTimeOffset now = _sessions.Now;
        // The day's first piece: its length is the day's allowance, the same on every device (ADR 88).
        SyncLunch first = LunchDay.Begin(now, length);
        await SaveAsync(_state with
        {
            Start = first.Start, End = first.End, OverTold = false, EditedAt = now, AllowanceMinutes = first.Allowance, Counted = first.Counted, Discarded = false,
        });
        StepAwayForLunch();
    }

    // Lunch takes the place of a break; during focus it pauses the session as a step away (always the paused kind, ADR 77), and a
    // step away with the timer running becomes lunch's.
    private void StepAwayForLunch()
    {
        if (!_sessions.EndBreakForLunch() && _sessions.State.IsActive && !_sessions.State.IsAway)
        {
            _sessions.StepAway(AwayKind.Break, keepTimerRunning: false);
        }
        else if (_sessions.State.AwayTimerRuns)
        {
            _sessions.HoldAwayForLunch();
        }
    }

    /// <summary>
    /// "Flexible this time" on the run-into-lunch card: today's fixed lunch doesn't start by itself, so the user presses
    /// Start lunch before going. Reminded at once, and the Today card says so.
    /// </summary>
    public async Task MakeFlexibleTodayAsync()
    {
        DateOnly today = DateOnly.FromDateTime(LocalNow());
        if (_state.NoAutoStartOn != today)
        {
            await SaveAsync(_state with { NoAutoStartOn = today });
        }

        _remind("Lunch won't start by itself today", "Press Start lunch before you go.");
    }

    /// <summary>+5 min (positive) or −5 min (negative) on the running lunch; it never ends sooner than a minute from now.</summary>
    public async Task AdjustAsync(TimeSpan change)
    {
        if (!IsOnLunch || Taken is not SyncLunch lunch)
        {
            return;
        }

        // The day's total never runs past its longest, never sooner than a minute from now, a −5 never makes it longer; the allowance moves with it (ADR 88).
        if (LunchDay.Adjust(lunch, _sessions.Now, change, (int)Length.TotalMinutes) is SyncLunch adjusted)
        {
            await SaveAsync(_state with { End = adjusted.End, OverTold = false, EditedAt = adjusted.EditedAt, AllowanceMinutes = adjusted.Allowance });
        }
    }

    /// <summary>"I'm back": the same as <see cref="CountAsync"/>. The minutes so far count and the rest of the day's lunch stays for later.</summary>
    public Task EndNowAsync() => CountAsync();

    /// <summary>
    /// "Count it" (ADR 88): lunch ends now, the minutes so far count and the rest of the day's allowance stays for a later piece; a session
    /// stepped away for it is back. False when no lunch runs.
    /// </summary>
    public async Task<bool> CountAsync()
    {
        if (!IsOnLunch || Taken is not SyncLunch lunch || LunchDay.Count(lunch, _sessions.Now) is not SyncLunch counted)
        {
            return false;
        }

        await SaveAsync(_state with { End = counted.End, OverTold = true, EditedAt = counted.EditedAt });
        ReturnSession();
        return true;
    }

    /// <summary>
    /// "Discard it" (ADR 88): the running piece never happened, so the minutes so far don't count and the full allowance (or the rest after
    /// the earlier counted pieces) is back. Refused (false) when no lunch runs or the active mode doesn't allow discarding (<see cref="DiscardAllowed"/>).
    /// A discarded lunch is still taken for the day, so a fixed lunch never starts again by itself. A session stepped away for it is back.
    /// </summary>
    public async Task<bool> DiscardAsync()
    {
        if (!IsOnLunch || Taken is not SyncLunch lunch || LunchDay.Discard(lunch, _sessions.Now, DiscardAllowed()) is not SyncLunch discarded)
        {
            return false;
        }

        await SaveAsync(_state with
        {
            Start = discarded.Start, End = discarded.End, OverTold = true, EditedAt = discarded.EditedAt, Counted = discarded.Counted, Discarded = discarded.Discarded,
        });
        ReturnSession();
        return true;
    }

    // A session stepped away for lunch is back (what "I'm back" always did; it never shortens a wait or unlocks anything).
    private void ReturnSession()
    {
        if (_sessions.State.IsActive && _sessions.State.IsAway)
        {
            _sessions.Return();
        }
    }

    /// <summary>Settings → Your day → "Lunch length on workdays": 15-120 minutes in steps of 5; the next lunch takes it.</summary>
    public async Task SetLengthAsync(int minutes)
    {
        int rounded = (int)Math.Round(minutes / (double)LengthStepMinutes) * LengthStepMinutes;
        int clamped = Math.Clamp(rounded, MinLengthMinutes, MaxLengthMinutes);
        if (clamped != _state.LengthMinutes)
        {
            await SaveAsync(_state with { LengthMinutes = clamped });
        }
    }

    /// <summary>
    /// A lunch another device (<paramref name="sender"/>) started or changed (its sync file): taken here when it's today's and its edit is newer than ours.
    /// True when it was taken (the card and the sync file follow at once; the caller redraws the day bar). A record identical to the one last received
    /// from that sender is ignored: the files are read again and again, and a clamped stamp (below) would otherwise be re-clamped to the newer "now"
    /// and beat an edit made here in between.
    /// </summary>
    public bool Receive(Guid sender, SyncLunch lunch)
    {
        if (_lastReceived.TryGetValue(sender, out SyncLunch? last) && last == lunch)
        {
            return false;
        }

        _lastReceived[sender] = lunch;
        if (!IsToday(lunch.Start))
        {
            return false;
        }

        SyncLunch received = lunch;

        // The stamp held to now, the day's total to the longest lunch, an older app's edit keeping our pieces, the newer edit winning (ADR 88).
        if (LunchDay.Receive(Taken, received, _sessions.Now) is not SyncLunch taken)
        {
            return false;
        }

        lunch = taken;
        bool wasOnLunch = IsOnLunch;
        _ = SaveAsync(_state with
        {
            Start = lunch.Start, End = lunch.End, EditedAt = lunch.EditedAt, OverTold = _sessions.Now >= lunch.End,
            AllowanceMinutes = lunch.Allowance, Counted = lunch.Counted, Discarded = lunch.Discarded,
        });

        // Started on another device (not a +5 or −5 on a lunch already running here): the same as pressing Start lunch here, so a break
        // ends and a running focus session steps away as a Break, as StartForAsync does. A received end changes nothing in the session.
        if (IsOnLunch && !wasOnLunch && !_sessions.EndBreakForLunch() && _sessions.State.IsActive && !_sessions.State.IsAway)
        {
            _sessions.StepAway(AwayKind.Break, keepTimerRunning: false);
        }
        else if (IsOnLunch && !wasOnLunch && _sessions.State.AwayTimerRuns)
        {
            _sessions.HoldAwayForLunch();
        }

        return true;
    }

    /// <summary>The hint, and "Lunch over!" once. Runs every second.</summary>
    public void CheckNow()
    {
        DateTimeOffset now = _sessions.Now;
        if (Taken is SyncLunch lunch && now >= lunch.End && !_state.OverTold)
        {
            _ = SaveAsync(_state with { OverTold = true });
            _chime();
            _remind("Lunch over!", "Welcome back. Plan your next task and start a session.");
            return;
        }

        DateTime local = LocalNow();
        DateOnly today = DateOnly.FromDateTime(local);
        if (AutoStartPending is FixedLunch fixedLunch && local >= fixedLunch.Window.Start && local + ShortestLeft <= fixedLunch.Window.End
            && !_sessions.State.IsActive && OtherPcSessionRunning?.Invoke() != true && _idle.GetIdleTime() < PresenceWindow)
        {
            // The first chance within the part's window (no session running here or on another PC, so a session that was kept has ended, and the user
            // is at the PC: input in the last couple of minutes; away, it waits for the first input; also when the app just
            // started): it starts as if Start lunch was pressed and ends where the part does.
            TimeSpan remaining = fixedLunch.Window.End - local;
            _ = StartForAsync(remaining < MaxLength ? remaining : MaxLength);
            return;
        }

        if (Today is FlexibleBreak flex && Taken is null && flex.HintAt is DateTime hint && local >= hint && local < flex.Window.End
            && _state.HintedOn != today)
        {
            _ = SaveAsync(_state with { HintedOn = today });
            _remind("Time for lunch?", $"Lunch isn't started yet. You usually take it before {flex.Window.End:t}: Start lunch on Today or your phone.");
        }
    }

    /// <summary>A lunch length as "1 h", "1 h 5 m" or "45 m".</summary>
    public static string FormatLength(TimeSpan length)
    {
        int totalMinutes = Math.Max(0, (int)Math.Round(length.TotalMinutes));
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        if (hours == 0)
        {
            return $"{minutes} m";
        }

        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} m";
    }

    private bool IsToday(DateTimeOffset time) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).DateTime) == DateOnly.FromDateTime(LocalNow());

    private DateTime LocalNow() => TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;

    private async Task SaveAsync(LunchState state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, state);
    }
}

/// <summary>
/// Today's lunch on this computer (document "lunch"): when it started and ends, when it was last edited (on any
/// device), whether "Lunch over!" came, the last hint's day, the lunch length setting in minutes, and the day "Flexible this time"
/// was chosen for (that day's fixed lunch doesn't start by itself; this computer only), and (ADR 88) the day's allowance in minutes, the
/// pieces counted before the current one and whether the only piece was discarded (what the sync file's lunch carries).
/// </summary>
public sealed record LunchState(
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    bool OverTold = false,
    DateOnly? HintedOn = null,
    DateTimeOffset? EditedAt = null,
    int LengthMinutes = 60,
    DateOnly? NoAutoStartOn = null,
    int? AllowanceMinutes = null,
    IReadOnlyList<LunchPiece>? Counted = null,
    bool Discarded = false);
