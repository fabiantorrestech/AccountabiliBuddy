using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Steers toward starting (redesign section C, "walk up to any device, have it steer me toward starting"): during the
/// day plan's work parts with no session running, the app opens Today with the task box ready when work starts, when
/// the app starts, and when the user comes back after <see cref="AwayFor"/> with no input. Once per occasion, never
/// while a session runs; it never blocks. On by default; Settings → Your day ("startNudge"). Use from the UI thread only.
/// </summary>
public sealed class StartNudgeService
{
    public static readonly TimeSpan AwayFor = TimeSpan.FromMinutes(10);
    private const string DocumentKey = "startNudge";

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly IUserIdleDetector _idle;
    private readonly SqliteJsonDocumentStore _store;
    private bool _wasInWork;
    private bool _sawAway;
    private bool _checkedOnce;
    private bool _sessionLastTick;
    private DateTimeOffset _lastNudge = DateTimeOffset.MinValue;

    public StartNudgeService(SessionController sessions, ConfigurationService configuration, IUserIdleDetector idle, SqliteJsonDocumentStore store)
    {
        _sessions = sessions;
        _configuration = configuration;
        _idle = idle;
        _store = store;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    public StartNudgeSettings Settings { get; private set; } = StartNudgeSettings.Default;

    /// <summary>True on a day off on this computer (ADR 31); then nothing steers. Set by the app.</summary>
    public Func<bool> IsDayOff { get; set; } = () => false;

    /// <summary>Time to steer: the app opens Today with the task box ready, or only flashes its taskbar button.</summary>
    public event EventHandler<StartNudgeReason>? Due;

    public void LoadBlocking()
    {
        try
        {
            Settings = Task.Run(() => _store.LoadAsync<StartNudgeSettings>(DocumentKey)).GetAwaiter().GetResult() ?? StartNudgeSettings.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = StartNudgeSettings.Default;
        }
    }

    public async Task SaveAsync(StartNudgeSettings settings)
    {
        Settings = settings;
        await _store.SaveAsync(DocumentKey, settings);
    }

    /// <summary>Runs every second.</summary>
    public void CheckNow()
    {
        bool sessionRunning = _sessions.State.IsActive;
        bool firstCheck = !_checkedOnce;
        bool sessionJustEnded = _sessionLastTick && !sessionRunning;
        _checkedOnce = true;
        _sessionLastTick = sessionRunning;

        bool inWork = Settings.On && !sessionRunning && !IsDayOff() && InWorkPart();
        if (!inWork)
        {
            _wasInWork = false;
            _sawAway = false;
            return;
        }

        if (!_wasInWork)
        {
            // Work just started, the app just started during it, or a session just ended inside it.
            _wasInWork = true;
            Nudge(firstCheck ? StartNudgeReason.AppStarted
                : sessionJustEnded ? StartNudgeReason.SessionEnded
                : StartNudgeReason.WorkStarted);
            return;
        }

        if (_idle.GetIdleTime() >= AwayFor)
        {
            _sawAway = true;
        }
        else if (_sawAway)
        {
            // Back at the desk.
            _sawAway = false;
            Nudge(StartNudgeReason.Back);
        }
        else if (Settings.RepeatMinutes > 0 && _sessions.Now - _lastNudge >= TimeSpan.FromMinutes(Settings.RepeatMinutes))
        {
            // Still no session: again, every few minutes, while someone's at the computer.
            Nudge(StartNudgeReason.Repeat);
        }
    }

    private void Nudge(StartNudgeReason reason)
    {
        _lastNudge = _sessions.Now;
        Due?.Invoke(this, reason);
    }

    private bool InWorkPart()
    {
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        return DayTimeline.Planned(_configuration.Current.DayPlan, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)))
            .Any(focus => focus.Window.Start <= now && now < focus.Window.End);
    }
}
