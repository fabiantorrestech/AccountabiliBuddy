using System.Globalization;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Steers toward starting (redesign section C, "walk up to any device, have it steer me toward starting"): during the
/// day plan's work parts with no session running, the timing starts when the user arrives, not when the part starts. A
/// part starting (or the app starting) while the user is away (idle <see cref="PresentWithin"/> or more) waits for the
/// first input; arriving, or coming back after <see cref="AwayFor"/> with no input, opens Today with the task box ready.
/// <see cref="CardAfter"/> after arriving with still no session, <see cref="Card"/> opens in front of everything (Start,
/// or Not now with a reason), and again every <see cref="StartNudgeSettings.RepeatMinutes"/> while the user is at the
/// computer. Never while a session runs, during a calendar event, on a day off, or once today's goal is met. On by
/// default; Settings → Your day ("startNudge"). Use from the UI thread only.
/// <para>
/// The first arrival of a workday (in the day's first guarded window, or before it while the user is at the computer) opens
/// <see cref="AssistantCard"/> (<see cref="StartOfDayAssistant"/>) at once instead of Today and the ordinary card, once a
/// day; its "Not now" comes back after the repeat like the card's. Later arrivals that day use the ordinary nudge.
/// </para>
/// </summary>
public sealed class StartNudgeService
{
    public static readonly TimeSpan AwayFor = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PresentWithin = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan CardAfter = TimeSpan.FromMinutes(5);
    private const string DocumentKey = "startNudge";

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly IUserIdleDetector _idle;
    private readonly SqliteJsonDocumentStore _store;
    private readonly StartNudgeCardLog _cardLog;
    private bool _wasInWork;
    private bool _checkedOnce;
    private bool _sessionLastTick;
    private bool _suppressedLastTick;
    // Null while waiting for the user to arrive (or come back); then the first input is the arrival.
    private DateTimeOffset? _arrivedAt;
    private bool _comingBack;
    private TimeSpan _lastIdle;
    private DateTimeOffset? _cardDueAt;
    // A reason half typed when the card was closed some other way; the reopened card shows it again.
    private string _keptReason = string.Empty;
    private DateOnly _prunedOn;
    // How long before the day's first guarded window the start-of-day assistant may open at the earliest.
    private static readonly TimeSpan AssistantLead = TimeSpan.FromHours(2);
    // The start-of-day assistant is next in line to open (instead of the ordinary card) until it is answered, or the user is gone.
    private bool _assistantFlow;
    private string _keptAssistantReason = string.Empty;

    public StartNudgeService(SessionController sessions, ConfigurationService configuration, IUserIdleDetector idle, SqliteJsonDocumentStore store, StartNudgeCardLog cardLog)
    {
        _sessions = sessions;
        _configuration = configuration;
        _idle = idle;
        _store = store;
        _cardLog = cardLog;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    public StartNudgeSettings Settings { get; private set; } = StartNudgeSettings.Default;

    /// <summary>True on a day off on this computer (ADR 31); then nothing steers. Set by the app.</summary>
    public Func<bool> IsDayOff { get; set; } = () => false;

    /// <summary>True during a calendar event (as lights out reads it); then nothing steers. Set by the app.</summary>
    public Func<bool> InCalendarEvent { get; set; } = () => false;

    /// <summary>True once today's deep-work goal is met; then nothing steers. Set by the app.</summary>
    public Func<bool> IsGoalMet { get; set; } = () => false;

    /// <summary>Counted focus on a date, for the day plan's Budget days (a met budget ends the day's work windows). Set by the app.</summary>
    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    /// <summary>The night before a date from the sleep settings, for a Budget day whose plan has no Sleep part. Set by the app.</summary>
    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>True while the work-hours gate shows; the card waits until it's closed. Set by the app.</summary>
    public Func<bool> IsGateOpen { get; set; } = () => false;

    /// <summary>The start-of-day assistant (hours, first tasks, Start). Set by the app.</summary>
    public StartOfDayAssistant? Assistant { get; set; }

    /// <summary>The assistant has opened at least once on this computer (or was turned off): nothing left to set up.</summary>
    public bool AssistantSeen => Assistant?.ShownOn is not null || !Settings.AssistantOn;

    /// <summary>The card showing, or null.</summary>
    public StartNudgeCardViewModel? Card { get; private set; }

    /// <summary>The start-of-day assistant's card showing, or null. Never together with <see cref="Card"/>.</summary>
    public StartOfDayCardViewModel? AssistantCard { get; private set; }

    /// <summary>Raised when <see cref="AssistantCard"/> opens or closes (the app shows or closes the window).</summary>
    public event EventHandler? AssistantCardChanged;

    /// <summary>Time to steer: the app opens Today with the task box ready, or only flashes its taskbar button.</summary>
    public event EventHandler<StartNudgeReason>? Due;

    /// <summary>Raised when <see cref="Card"/> opens or closes (the app shows or closes the window).</summary>
    public event EventHandler? CardChanged;

    public void LoadBlocking()
    {
        try
        {
            StartNudgeSettings? stored = Task.Run(() => _store.LoadAsync<StartNudgeSettings>(DocumentKey)).GetAwaiter().GetResult();
            Settings = stored ?? StartNudgeSettings.Default;
            if (stored is not null && stored.Version < StartNudgeSettings.CurrentVersion)
            {
                // The repeat used to flash the taskbar; now it brings the card in front, so an older choice becomes 15 once.
                Settings = stored with { RepeatMinutes = 15, Version = StartNudgeSettings.CurrentVersion };
                Task.Run(() => _store.SaveAsync(DocumentKey, Settings)).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = StartNudgeSettings.Default;
        }

        PruneLog();
    }

    public async Task SaveAsync(StartNudgeSettings settings)
    {
        Settings = settings;
        await _store.SaveAsync(DocumentKey, settings);
    }

    /// <summary>The card's window was closed some other way (no Start, no reason): it opens again at the next check.</summary>
    public void CardWindowClosed()
    {
        if (Card is null)
        {
            return;
        }

        _keptReason = Card.Reason;
        Card = null;
        _cardDueAt = _sessions.Now;
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The assistant's window was closed some other way (no Start, no reason): it opens again at the next check.</summary>
    public void AssistantWindowClosed()
    {
        if (AssistantCard is null)
        {
            return;
        }

        _keptAssistantReason = AssistantCard.Reason;
        AssistantCard.Dispose();
        AssistantCard = null;
        _cardDueAt = _sessions.Now;
        AssistantCardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs every second.</summary>
    public void CheckNow()
    {
        bool sessionRunning = _sessions.State.IsActive;
        bool firstCheck = !_checkedOnce;
        bool sessionJustEnded = _sessionLastTick && !sessionRunning;
        _checkedOnce = true;
        _sessionLastTick = sessionRunning;
        if (Today() != _prunedOn)
        {
            PruneLog(); // Old reasons go after 14 days even when no new one is added.
        }

        // A calendar event, lunch (a day off counts it) or the goal holding the nudge back: when it lifts, work didn't
        // just start, so the nudge after it is quiet.
        bool suppressed = IsDayOff() || InCalendarEvent() || IsGoalMet();
        bool suppressionJustLifted = _suppressedLastTick && !suppressed;
        _suppressedLastTick = suppressed;
        if (sessionRunning && _sessions.State.IsRealSession)
        {
            Assistant?.MarkDone(Today()); // A session the user started is the day's start; a test session is not.
        }

        // Before the day's first window the assistant already waits for the user (the ordinary nudge does not).
        LocalWindow? part = Settings.On && !sessionRunning && !suppressed
            ? CurrentWorkPart() ?? UpcomingWindowForAssistant()
            : null;
        if (part is not LocalWindow workPart)
        {
            Reset();
            return;
        }

        Card?.Refresh(_sessions.Now);
        TimeSpan idle = _idle.GetIdleTime();
        if (!_wasInWork)
        {
            // Work just started, the app just started during it, a session just ended inside it, or a calendar event,
            // lunch or the goal stopped holding the nudge back inside it.
            _wasInWork = true;
            if (sessionJustEnded)
            {
                // Counts as arrived, so no loud nudge follows just because it ended while idle; quiet: the user may be typing elsewhere.
                // A test session ending is not the day's start: no assistant.
                Arrive(assistantAllowed: false, quiet: true);
                Nudge(StartNudgeReason.SessionEnded);
            }
            else if (suppressionJustLifted)
            {
                // The same quiet way: the user may be typing elsewhere (closing meeting notes, back from lunch).
                Arrive(assistantAllowed: true, quiet: true);
                Nudge(StartNudgeReason.Resumed);
            }
            else if (idle < PresentWithin)
            {
                Arrive(assistantAllowed: true, quiet: false);
                Nudge(firstCheck ? StartNudgeReason.AppStarted : StartNudgeReason.WorkStarted);
            }
            else
            {
                _lastIdle = idle; // Away: the timing starts with the first input.
            }

            return;
        }

        if (_arrivedAt is null)
        {
            // The idle time only drops when there was input: the user just arrived or came back.
            if (idle < _lastIdle)
            {
                Arrive(assistantAllowed: true, quiet: false);
                Nudge(_comingBack ?StartNudgeReason.Back : StartNudgeReason.WorkStarted);
            }

            _lastIdle = idle;
            return;
        }

        if (idle >= AwayFor)
        {
            // Gone: wait for them to come back. A card already showing stays in front.
            _arrivedAt = null;
            _cardDueAt = null;
            _comingBack = true;
            _lastIdle = idle;
            if (AssistantCard is null)
            {
                _assistantFlow = false; // Coming back later is a later arrival: the ordinary nudge.
            }

            return;
        }

        // Only in front of someone at the computer, and never over the work-hours gate: otherwise it stays due.
        if (Card is null && AssistantCard is null && _cardDueAt is DateTimeOffset due && _sessions.Now >= due && idle < PresentWithin && !IsGateOpen())
        {
            if (_assistantFlow && Assistant is not null)
            {
                OpenAssistantCard();
            }
            else if (workPart.Start <= TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime)
            {
                OpenCard(workPart); // Not before the window starts: only the assistant comes early.
            }
        }
    }

    // The first arrival of the day with the assistant on opens it at once; every other arrival waits the usual few minutes.
    private void Arrive(bool assistantAllowed, bool quiet)
    {
        _arrivedAt = _sessions.Now;
        _comingBack = false;
        _assistantFlow = _assistantFlow || (assistantAllowed && AssistantDue() && AssistantMayOpenNow());
        if (Card is null && AssistantCard is null)
        {
            _cardDueAt = _sessions.Now + (_assistantFlow && !quiet ? TimeSpan.Zero : CardAfter);
        }
    }

    private bool AssistantDue() => Settings.AssistantOn && Assistant is not null && !Assistant.IsDoneOn(Today());

    private void OpenAssistantCard()
    {
        _cardDueAt = null;
        Assistant!.MarkDone(Today());
        AssistantCard = Assistant.CreateCard(Settings.RepeatMinutes > 0, OnAssistantStartAsync, OnAssistantNotNow);
        AssistantCard.Reason = _keptAssistantReason;
        _keptAssistantReason = string.Empty;
        AssistantCardChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> OnAssistantStartAsync(StartOfDayCardViewModel card)
    {
        if (card != AssistantCard || Assistant is null)
        {
            return false;
        }

        bool started = await Assistant.StartAsync();
        if (started)
        {
            _assistantFlow = false;
            CloseAssistantCard(scheduleNext: false);
        }

        return started;
    }

    private void OnAssistantNotNow(StartOfDayCardViewModel card, string reason)
    {
        if (card != AssistantCard || !reason.Any(char.IsLetterOrDigit))
        {
            return;
        }

        _ = _cardLog.AddAsync(_sessions.Now, reason);
        CloseAssistantCard(scheduleNext: true);
    }

    // Next card after the repeat (none for "once"); it is the assistant again until a session starts.
    private void CloseAssistantCard(bool scheduleNext)
    {
        if (AssistantCard is null)
        {
            return;
        }

        AssistantCard.Dispose();
        AssistantCard = null;
        _keptAssistantReason = string.Empty;
        _cardDueAt = scheduleNext && Settings.RepeatMinutes > 0 ? _sessions.Now + TimeSpan.FromMinutes(Settings.RepeatMinutes) : null;
        if (_cardDueAt is null)
        {
            _assistantFlow = false;
        }

        AssistantCardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenCard(LocalWindow workPart)
    {
        _cardDueAt = null;
        string until = $"Work until {workPart.End.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
        Card = new StartNudgeCardViewModel(until, _sessions.Now, _sessions.TimeProvider.LocalTimeZone, Settings.RepeatMinutes > 0, OnCardStart, OnCardNotNow)
        {
            Reason = _keptReason,
        };
        _keptReason = string.Empty;
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCardStart(StartNudgeCardViewModel card)
    {
        if (card != Card)
        {
            return;
        }

        CloseCard(scheduleNext: true);
        Nudge(StartNudgeReason.CardStart);
    }

    private void OnCardNotNow(StartNudgeCardViewModel card, string reason)
    {
        if (card != Card || !reason.Any(char.IsLetterOrDigit))
        {
            return;
        }

        _ = _cardLog.AddAsync(_sessions.Now, reason);
        CloseCard(scheduleNext: true);
    }

    // Next card after the repeat (none for "once"), as long as the user stays and no session starts.
    private void CloseCard(bool scheduleNext)
    {
        if (Card is null)
        {
            return;
        }

        Card = null;
        _keptReason = string.Empty;
        _cardDueAt = scheduleNext && Settings.RepeatMinutes > 0 ? _sessions.Now + TimeSpan.FromMinutes(Settings.RepeatMinutes) : null;
        CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Reset()
    {
        _wasInWork = false;
        _arrivedAt = null;
        _comingBack = false;
        CloseCard(scheduleNext: false);
        CloseAssistantCard(scheduleNext: false);
        _assistantFlow = false;
        _cardDueAt = null;
        _keptReason = string.Empty;
        _keptAssistantReason = string.Empty;
    }

    private void PruneLog()
    {
        _prunedOn = Today();
        _ = _cardLog.PruneAsync(_sessions.Now);
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime);

    private void Nudge(StartNudgeReason reason)
    {
        if (_assistantFlow && reason is not (StartNudgeReason.SessionEnded or StartNudgeReason.Resumed))
        {
            return; // The assistant is the greeting: no Today window behind it.
        }

        Due?.Invoke(this, reason);
    }

    // The day's first guarded window when it has not begun yet and the assistant still has to come (or is up): a user
    // arriving before work starts gets it then, but only once the day has started (see AssistantMayOpenNow). Null once the
    // day's work has begun, on a day with no guarded window (a free day), when it is too early, or when the assistant is
    // off or has come.
    private LocalWindow? UpcomingWindowForAssistant()
    {
        if (!Settings.AssistantOn || Assistant is null || !(AssistantDue() || _assistantFlow))
        {
            return null;
        }

        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        IReadOnlyList<PlannedFocus> today = DayWorkWindows.WorkWindows(_configuration.Current.DayPlan, new LocalWindow(now.Date, now.Date.AddDays(1)), now, FocusedOn, NightFallback);
        if (today.Count == 0)
        {
            return null;
        }

        LocalWindow first = today.Select(focus => focus.Window).MinBy(window => window.Start);
        if (first.Start <= now)
        {
            return null;
        }

        // An assistant already up stays up; a new one waits for the day to start, so night-time input never uses it up.
        return _assistantFlow || AssistantCard is not null || AssistantMayOpenNow() ? first : null;
    }

    // The assistant opens from the day's wake time (the plan's night, else the sleep settings') or at most
    // AssistantLead before the first guarded window, whichever is later, and never during a Sleep part. With no wake
    // time known and no window to count back from, not before 5:00 AM.
    private bool AssistantMayOpenNow()
    {
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        DayPlan plan = _configuration.Current.DayPlan;
        DateOnly today = DateOnly.FromDateTime(now);

        bool asleep = DayWorkWindows.PartsIn(plan, new LocalWindow(now, now.AddMinutes(1)), now, FocusedOn, NightFallback)
            .Any(part => part.Category.Behavior == CategoryBehavior.Sleep && part.Window.Contains(now));
        if (asleep)
        {
            return false;
        }

        DateTime? wake = (DayPlanSleep.NightBefore(plan, today) ?? NightFallback?.Invoke(today)) is SleepTimes night
            ? now.Date + night.Wake.ToTimeSpan()
            : null;

        // Windows that begin today; the part of last night's window after midnight is not the day's start.
        DateTime? firstStart = DayWorkWindows.WorkWindows(plan, new LocalWindow(now.Date, now.Date.AddDays(1)), now, FocusedOn, NightFallback)
            .Select(focus => focus.Window.Start)
            .Where(start => start > now.Date)
            .Cast<DateTime?>()
            .Min();
        DateTime? leadFrom = firstStart - AssistantLead;

        DateTime openFrom = (wake, leadFrom) switch
        {
            ({ } w, { } l) => w > l ? w : l,
            ({ } w, null) => w,
            (null, { } l) => l,
            _ => now.Date.AddHours(5),
        };
        return now >= openFrom;
    }

    private LocalWindow? CurrentWorkPart()
    {
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        return DayWorkWindows.WorkWindows(_configuration.Current.DayPlan, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)), now, FocusedOn, NightFallback)
            .Where(focus => focus.Window.Start <= now && now < focus.Window.End)
            .Select(focus => (LocalWindow?)focus.Window)
            .FirstOrDefault();
    }
}
