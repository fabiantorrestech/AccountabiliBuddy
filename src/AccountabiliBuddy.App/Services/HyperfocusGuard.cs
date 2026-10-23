using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The hyperfocus guard (redesign phase 2): one quiet reminder after <see cref="HyperfocusSettings.IntervalMinutes"/> of
/// unbroken focus ("90 minutes in · water, stand, eat"). A stretch starts with a focus phase and again on coming back
/// from Step away; it gets one reminder; breaks and time away get none, and profiles in
/// <see cref="HyperfocusSettings.OffForProfiles"/> never do (documents key "hyperfocus"). Use from the UI thread only.
/// </summary>
public sealed class HyperfocusGuard : IDisposable
{
    private const string DocumentKey = "hyperfocus";

    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Action<string, string> _remind;
    private SessionState _last = SessionState.Idle;
    private DateTimeOffset? _stretchStart;
    private bool _reminded;

    /// <param name="remind">Shows the reminder (title, message).</param>
    public HyperfocusGuard(SessionController sessions, SqliteJsonDocumentStore store, Action<string, string> remind)
    {
        _sessions = sessions;
        _store = store;
        _remind = remind;
        _sessions.StateChanged += OnStateChanged;
        _sessions.Ticked += OnTicked;
    }

    public HyperfocusSettings Settings { get; private set; } = HyperfocusSettings.Default;

    public void LoadBlocking()
    {
        try
        {
            HyperfocusSettings? loaded = Task.Run(() => _store.LoadAsync<HyperfocusSettings>(DocumentKey)).GetAwaiter().GetResult();
            Settings = loaded is null ? HyperfocusSettings.Default : loaded with { OffForProfiles = loaded.OffForProfiles ?? Array.Empty<Guid>() };
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = HyperfocusSettings.Default;
        }
    }

    public async Task SaveAsync(HyperfocusSettings settings)
    {
        Settings = settings with { IntervalMinutes = Math.Max(0, settings.IntervalMinutes) };
        await _store.SaveAsync(DocumentKey, Settings);
    }

    /// <summary>Reminds once the stretch is long enough. Runs every second.</summary>
    public void CheckNow()
    {
        Track(_sessions.State);
        SessionState state = _sessions.State;
        if (_reminded || _stretchStart is not DateTimeOffset start || !state.IsEnforcing || state.IsAway
            || Settings.IntervalMinutes <= 0 || state.ProfileId is not Guid profileId || Settings.OffForProfiles.Contains(profileId))
        {
            return;
        }

        TimeSpan interval = TimeSpan.FromMinutes(Settings.IntervalMinutes);
        if (_sessions.Now - start >= interval)
        {
            _reminded = true;
            _remind($"{Settings.IntervalMinutes} minutes in", "Water, stand, eat. Step away pauses the timer.");
        }
    }

    public void Dispose()
    {
        _sessions.StateChanged -= OnStateChanged;
        _sessions.Ticked -= OnTicked;
    }

    // A new focus phase or a return from Step away starts a stretch; anything else that isn't focus ends it.
    private void Track(SessionState state)
    {
        bool focusing = state.IsEnforcing && !state.IsAway;
        bool wasFocusing = _last.IsEnforcing && !_last.IsAway;
        bool newPhase = state.Phase != _last.Phase || state.CompletedFocusCycles != _last.CompletedFocusCycles || state.Mode != _last.Mode
            || state.ProfileId != _last.ProfileId;
        if (focusing && (!wasFocusing || newPhase))
        {
            // A session resumed after a restart or a fresh phase counts from its start; a return from away counts from now.
            _stretchStart = _last.IsAway || state.PhaseStartedAt is null ? _sessions.Now : state.PhaseStartedAt;
            _reminded = false;
        }
        else if (!focusing)
        {
            _stretchStart = null;
        }

        _last = state;
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        Track(state);
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
    }
}
