using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The flexible lunch (schema v8, design part B): on any workday "Start lunch" (on any of the user's devices) starts it
/// for the lunch length (Settings → Your day, 1 hour by default); while it runs, +5 / −5 min change its end. A flexible
/// break in today's plan is only a guideline: its window is drawn on the day bar and gives the hint time. A running
/// session steps away as a Break for it. Not started by its hint time: one hint; never started: skipped (the window
/// stays work). When it's over: one chime and "Lunch over!". A lunch started or changed on another device arrives in
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

    /// <summary>How much +5 / −5 min changes a running lunch by.</summary>
    public static readonly TimeSpan AdjustStep = TimeSpan.FromMinutes(5);

    /// <summary>A shortened lunch ends no sooner than this after now.</summary>
    private static readonly TimeSpan ShortestLeft = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Action<string, string> _remind;
    private readonly Action _chime;
    private LunchState _state = new();

    public LunchService(SessionController sessions, ConfigurationService configuration, SqliteJsonDocumentStore store, Action<string, string> remind, Action chime)
    {
        _sessions = sessions;
        _configuration = configuration;
        _store = store;
        _remind = remind;
        _chime = chime;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    /// <summary>Raised when lunch starts, changes or ends, and when the length setting changes (the Today card, the day bar and the sync file follow).</summary>
    public event EventHandler? Changed;

    /// <summary>Today's flexible break from the plan (the usual lunch window and the hint); null on a day without one.</summary>
    public FlexibleBreak? Today => _configuration.Current.DayPlan.FlexibleBreakOn(DateOnly.FromDateTime(LocalNow()));

    /// <summary>
    /// True on a workday: one of the plan's workdays, or a one-off day with work in it. A one-off day replaces a
    /// workday's template, so a one-off day off on a Monday isn't one.
    /// </summary>
    public bool IsWorkdayToday
    {
        get
        {
            DayPlan plan = _configuration.Current.DayPlan;
            DateOnly today = DateOnly.FromDateTime(LocalNow());
            OneOffDay? oneOff = plan.OneOffDays.FirstOrDefault(day => day.Date == today);
            if (oneOff is null)
            {
                return plan.Workdays.Contains(today.DayOfWeek);
            }

            return oneOff.Template.Parts.Any(part => plan.Category(part.CategoryId)?.Behavior.IsWork() == true);
        }
    }

    /// <summary>How long a lunch lasts when it starts (the setting; it wins over a flexible break's own length).</summary>
    public TimeSpan Length => TimeSpan.FromMinutes(Math.Clamp(_state.LengthMinutes, MinLengthMinutes, MaxLengthMinutes));

    /// <summary>Today's lunch once started (it may be over); null before it starts.</summary>
    public SyncLunch? Taken => _state.Start is DateTimeOffset start && _state.End is DateTimeOffset end && IsToday(start)
        ? new SyncLunch(start, end, _state.EditedAt)
        : null;

    /// <summary>True while lunch runs.</summary>
    public bool IsOnLunch => Taken is SyncLunch lunch && _sessions.Now < lunch.End;

    /// <summary>"Start lunch" shows: today is a workday (or has a flexible break) and no lunch was taken yet.</summary>
    public bool CanStart => (IsWorkdayToday || Today is not null) && Taken is null;

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

    /// <summary>Starts lunch now for the lunch length; a running session steps away.</summary>
    public async Task StartAsync()
    {
        DateTimeOffset now = _sessions.Now;
        await SaveAsync(_state with { Start = now, End = now + Length, OverTold = false, EditedAt = now });
        if (_sessions.State.IsActive && !_sessions.State.IsAway)
        {
            _sessions.StepAway(AwayKind.Break);
        }
    }

    /// <summary>+5 min (positive) or −5 min (negative) on the running lunch; it never ends sooner than a minute from now.</summary>
    public async Task AdjustAsync(TimeSpan change)
    {
        if (!IsOnLunch || _state.End is not DateTimeOffset end)
        {
            return;
        }

        DateTimeOffset now = _sessions.Now;
        DateTimeOffset newEnd = end + change;
        if (newEnd < now + ShortestLeft)
        {
            newEnd = now + ShortestLeft;
        }

        if (newEnd != end)
        {
            await SaveAsync(_state with { End = newEnd, OverTold = false, EditedAt = now });
        }
    }

    /// <summary>Back early: lunch ends now.</summary>
    public async Task EndNowAsync()
    {
        if (IsOnLunch)
        {
            DateTimeOffset now = _sessions.Now;
            await SaveAsync(_state with { End = now, OverTold = true, EditedAt = now });
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

    /// <summary>A lunch another device started or changed (its sync file): taken here when it's today's and its edit is newer than ours.</summary>
    public void Receive(SyncLunch lunch)
    {
        if (!IsToday(lunch.Start))
        {
            return;
        }

        if (Taken is SyncLunch ours && (ours.LastEdit >= lunch.LastEdit || (ours.Start == lunch.Start && ours.End == lunch.End)))
        {
            return;
        }

        _ = SaveAsync(_state with { Start = lunch.Start, End = lunch.End, EditedAt = lunch.EditedAt, OverTold = _sessions.Now >= lunch.End });
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
/// device), whether "Lunch over!" came, the last hint's day, and the lunch length setting in minutes.
/// </summary>
public sealed record LunchState(
    DateTimeOffset? Start = null,
    DateTimeOffset? End = null,
    bool OverTold = false,
    DateOnly? HintedOn = null,
    DateTimeOffset? EditedAt = null,
    int LengthMinutes = 60);
