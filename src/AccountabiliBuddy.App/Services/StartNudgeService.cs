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

    public StartNudgeService(SessionController sessions, ConfigurationService configuration, IUserIdleDetector idle, SqliteJsonDocumentStore store)
    {
        _sessions = sessions;
        _configuration = configuration;
        _idle = idle;
        _store = store;
        _sessions.Ticked += (_, _) => CheckNow();
    }

    public StartNudgeSettings Settings { get; private set; } = StartNudgeSettings.Default;

    /// <summary>Time to steer: the app opens Today with the task box ready.</summary>
    public event EventHandler? Due;

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
        bool inWork = Settings.On && !_sessions.State.IsActive && InWorkPart();
        if (!inWork)
        {
            _wasInWork = false;
            _sawAway = false;
            return;
        }

        if (!_wasInWork)
        {
            // Work just started, or the app just started during it.
            _wasInWork = true;
            Due?.Invoke(this, EventArgs.Empty);
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
            Due?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool InWorkPart()
    {
        DateTime now = TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
        return DayTimeline.Planned(_configuration.Current.DayPlan, new LocalWindow(now.Date.AddDays(-1), now.Date.AddDays(2)))
            .Any(focus => focus.Window.Start <= now && now < focus.Window.End);
    }
}
