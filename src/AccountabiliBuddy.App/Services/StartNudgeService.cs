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

    /// <summary>True while the work-hours gate shows; the card waits until it's closed. Set by the app.</summary>
    public Func<bool> IsGateOpen { get; set; } = () => false;

    /// <summary>The card showing, or null.</summary>
    public StartNudgeCardViewModel? Card { get; private set; }

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
        LocalWindow? part = Settings.On && !sessionRunning && !suppressed
            ? CurrentWorkPart()
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
                Arrive();
                Nudge(StartNudgeReason.SessionEnded);
            }
            else if (suppressionJustLifted)
            {
                // The same quiet way: the user may be typing elsewhere (closing meeting notes, back from lunch).
                Arrive();
                Nudge(StartNudgeReason.Resumed);
            }
            else if (idle < PresentWithin)
            {
                Arrive();
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
                Arrive();
                Nudge(_comingBack ? StartNudgeReason.Back : StartNudgeReason.WorkStarted);
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
            return;
        }

        // Only in front of someone at the computer, and never over the work-hours gate: otherwise it stays due.
        if (Card is null && _cardDueAt is DateTimeOffset due && _sessions.Now >= due && idle < PresentWithin && !IsGateOpen())
        {
            OpenCard(workPart);
        }
    }

    private void Arrive()
    {
        _arrivedAt = _sessions.Now;
        _comingBack = false;
        if (Card is null)
        {
            _cardDueAt = _sessions.Now + CardAfter;
        }
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
        _cardDueAt = null;
        _keptReason = string.Empty;
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
        Due?.Invoke(this, reason);
    }

    private LocalWindow? CurrentWorkPart()
    {
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        return DayWorkWindows.WorkWindows(_configuration.Current.DayPlan, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)), now, FocusedOn)
            .Where(focus => focus.Window.Start <= now && now < focus.Window.End)
            .Select(focus => (LocalWindow?)focus.Window)
            .FirstOrDefault();
    }
}
