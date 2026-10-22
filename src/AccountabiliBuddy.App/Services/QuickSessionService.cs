using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The quick-session popup: opened by the global shortcut, the Start menu shortcut or the tray.
/// Shows a blurred snapshot of the monitor under the cursor with the popup centred on it, starts the chosen
/// session, remembers the choices for next time, and reopens ("start another?") when a session it started
/// finishes on its own. The Session tab uses the same start controls (<see cref="CreateStartControls"/>) and
/// shares the remembered choices. Use from the UI thread only (the hotkey event is marshalled here).
/// </summary>
public sealed class QuickSessionService
{
    private const string PreferencesKey = "quickSession";
    private const string CountUpDefaultName = "Quick Start";

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SessionEndFlow _endFlow;
    private readonly IMonitorLocator _monitors;
    private readonly IScreenCapture _screenCapture;
    private readonly IGlobalHotkey _hotkey;
    private readonly SqliteJsonDocumentStore _store;

    private QuickSessionWindow? _window;
    private BlurBackdrop? _backdrop;

    // True while a session started from the popup is running, so its natural end can offer another one.
    private bool _ownsRunningSession;

    /// <summary>Guided sessions: the checklist the start controls edit before Start; set by the app.</summary>
    public SessionTasksService? Tasks { get; set; }

    public QuickSessionService(
        SessionController sessions,
        ConfigurationService configuration,
        SessionEndFlow endFlow,
        IMonitorLocator monitors,
        IScreenCapture screenCapture,
        IGlobalHotkey hotkey,
        SqliteJsonDocumentStore store)
    {
        _sessions = sessions;
        _configuration = configuration;
        _endFlow = endFlow;
        _monitors = monitors;
        _screenCapture = screenCapture;
        _hotkey = hotkey;
        _store = store;

        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(Toggle);
        _sessions.SessionCompleted += OnSessionCompleted;
        _sessions.StateChanged += (_, state) =>
        {
            if (!state.IsActive)
            {
                // Deferred: the engine raises StateChanged before SessionCompleted, which still needs the flag.
                Dispatcher.UIThread.Post(() => _ownsRunningSession = false);
            }
        };
    }

    public QuickSessionPreferences Preferences { get; private set; } = QuickSessionPreferences.Default;

    /// <summary>The profile "Count up" starts: the one chosen in Settings while it exists, else null ("Quick Start", made on first use).</summary>
    public Guid? CountUpProfileId => Preferences.CountUpProfileId is Guid id && _configuration.Current.Profiles.Any(profile => profile.Id == id)
        ? id
        : null;

    public string CountUpProfileName => ProfileOptions.NameOf(_configuration.Current.Profiles, CountUpProfileId) ?? CountUpDefaultName;

    /// <summary>Why the global shortcut is not active, or null when it is.</summary>
    public string? HotkeyError { get; private set; }

    /// <summary>Loads saved choices and registers the shortcut. Called once at startup.</summary>
    public void LoadBlocking()
    {
        try
        {
            Preferences = Task.Run(() => _store.LoadAsync<QuickSessionPreferences>(PreferencesKey)).GetAwaiter().GetResult()
                ?? QuickSessionPreferences.Default;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Preferences = QuickSessionPreferences.Default;
        }

        RegisterHotkey(Preferences.Hotkey);
    }

    /// <returns>Null on success, otherwise why the shortcut could not be used (the previous one stays active).</returns>
    public async Task<string?> ChangeHotkeyAsync(string text)
    {
        if (!HotkeyChord.TryParse(text, out HotkeyChord? chord, out string? parseError))
        {
            return parseError;
        }

        if (!_hotkey.TryRegister(chord, out string? registerError))
        {
            RegisterHotkey(Preferences.Hotkey); // Put the old one back.
            return registerError;
        }

        HotkeyError = null;
        Preferences = Preferences with { Hotkey = chord.ToString() };
        await SavePreferencesAsync();
        return null;
    }

    public void Show()
    {
        Show(sessionCompleted: false);
    }

    /// <summary>Start controls for the Session tab, with the last choices; Start starts the session there.</summary>
    public QuickSessionViewModel CreateStartControls()
    {
        var viewModel = new QuickSessionViewModel(
            _configuration.Current.Profiles, Preferences, sessionCompleted: false, runningSessionText: null, _sessions.TimeProvider);
        viewModel.StartRequested += async (_, _) => await StartAsync(viewModel, fromPopup: false);
        viewModel.Checklist = Tasks is null ? null : new ChecklistViewModel(Tasks, ChecklistMode.Draft, _sessions.TimeProvider);
        viewModel.CountUpLabel = $"Count up · {CountUpProfileName}";
        viewModel.CountUpRequested += async (_, _) => await StartCountUpAsync();
        return viewModel;
    }

    /// <summary>Shortcut pressed: open the popup, or close it if it is already open.</summary>
    public void Toggle()
    {
        if (_window is not null)
        {
            CloseAll();
        }
        else
        {
            Show();
        }
    }

    private void Show(bool sessionCompleted)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        string monitorId = _monitors.FindMonitorUnderCursor();
        MonitorArea? area = _monitors.Find(monitorId);
        if (area is null)
        {
            return;
        }

        SessionState state = _sessions.State;
        string? runningText = state.IsActive
            ? $"{SessionStatusFormatter.Title(state, ProfileOptions.NameOf(_configuration.Current.Profiles, state.ProfileId))} · "
              + SessionStatusFormatter.Detail(state, _sessions.Now)
            : null;

        var viewModel = new QuickSessionViewModel(
            _configuration.Current.Profiles, Preferences, sessionCompleted, runningText, _sessions.TimeProvider);
        viewModel.StartRequested += async (_, _) => await StartAsync(viewModel, fromPopup: true);
        viewModel.Checklist = Tasks is null ? null : new ChecklistViewModel(Tasks, ChecklistMode.Draft, _sessions.TimeProvider);
        viewModel.CountUpLabel = $"Count up · {CountUpProfileName}";
        viewModel.CountUpRequested += async (_, _) => await StartCountUpAsync();
        viewModel.EndSessionRequested += async (_, _) =>
        {
            CloseAll();
            await _endFlow.TryEndSessionAsync(owner: null);
        };
        viewModel.CloseRequested += (_, _) => CloseAll();

        // Snapshot first, so the backdrop shows the screen as it was, not with our windows on it.
        _backdrop = new BlurBackdrop();
        _backdrop.SetSnapshot(_screenCapture.Capture(area.Bounds));
        _backdrop.Dismissed += (_, _) => CloseAll();
        MonitorPlacement.Cover(_backdrop, area);

        _window = new QuickSessionWindow { DataContext = viewModel };
        _window.Closed += (_, _) => viewModel.Dispose();
        _window.FocusLost += (_, _) => CloseAll();
        MonitorPlacement.Center(_window, area);

        _backdrop.Show();
        _window.Show();
    }

    /// <param name="fromPopup">Only a session started from the popup reopens it ("start another?") when it ends.</param>
    private async Task StartAsync(QuickSessionViewModel viewModel, bool fromPopup)
    {
        if (viewModel.SelectedProfile is not { } profile)
        {
            return;
        }

        // Exact length: a chosen end time ends the session at that time, not on a rounded minute.
        TimeSpan focus = viewModel.FocusLength;
        bool started = viewModel.BreakEnabled
            ? _sessions.StartFocusThenBreak(profile.Id, focus, TimeSpan.FromMinutes(viewModel.WholeBreakMinutes))
            : _sessions.StartManual(profile.Id, focus);

        CloseAll();
        if (!started)
        {
            return;
        }

        _ownsRunningSession = fromPopup;
        Preferences = viewModel.ToPreferences(Preferences.Hotkey) with { CountUpProfileId = Preferences.CountUpProfileId };
        await SavePreferencesAsync();
    }

    /// <summary>Raised when the count-up profile was chosen (Settings) or made on first use.</summary>
    public event EventHandler? CountUpProfileChanged;

    /// <summary>Settings: the profile "Count up" starts; null = "Quick Start", made on first use.</summary>
    public async Task SetCountUpProfileAsync(Guid? profileId)
    {
        Preferences = Preferences with { CountUpProfileId = profileId };
        await SavePreferencesAsync();
        CountUpProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts the count-up profile with no set end; the on-screen bar counts up (it never runs out on its own).</summary>
    /// <returns>True when it started (also used by linked devices).</returns>
    public async Task<bool> StartCountUpAsync()
    {
        CloseAll();
        return await EnsureCountUpProfileAsync() is Guid profileId && _sessions.StartManual(profileId, duration: null);
    }

    /// <summary>
    /// The count-up profile, made on first use when none is chosen: "Quick Start", starting from the rules of the
    /// profile used last (or the example's), so it blocks what you usually block. Edit it in Profiles.
    /// </summary>
    private async Task<Guid?> EnsureCountUpProfileAsync()
    {
        if (CountUpProfileId is Guid existing)
        {
            return existing;
        }

        if (_configuration.IsReadOnly)
        {
            return null;
        }

        FocusProfile template = _configuration.Current.Profiles.FirstOrDefault(profile => profile.Id == Preferences.ProfileId)
            ?? _configuration.Current.Profiles.FirstOrDefault()
            ?? ExampleProfiles.Coding();
        FocusProfile quickStart = template with
        {
            Id = Guid.NewGuid(),
            Name = CountUpDefaultName,
            Rules = template.Rules.Select(rule => rule with { Id = Guid.NewGuid() }).ToList(),
            UnlockOptions = template.UnlockOptions.Select(option => option with { Id = Guid.NewGuid() }).ToList(),
        };
        await _configuration.UpdateAsync(document => document with { Profiles = document.Profiles.Append(quickStart).ToList() });
        if (_configuration.Current.Profiles.All(profile => profile.Id != quickStart.Id))
        {
            // Held back (a looser change waiting): nothing to start yet.
            return null;
        }

        await SetCountUpProfileAsync(quickStart.Id);
        return quickStart.Id;
    }

    private void OnSessionCompleted(object? sender, SessionState finished)
    {
        if (_ownsRunningSession)
        {
            _ownsRunningSession = false;
            Show(sessionCompleted: true);
        }
    }

    private void CloseAll()
    {
        QuickSessionWindow? window = _window;
        BlurBackdrop? backdrop = _backdrop;
        _window = null;
        _backdrop = null;
        window?.Close();
        backdrop?.Close();
    }

    private void RegisterHotkey(string text)
    {
        HotkeyChord chord = HotkeyChord.TryParse(text, out HotkeyChord? parsed, out _) ? parsed : HotkeyChord.Default;
        HotkeyError = _hotkey.TryRegister(chord, out string? error) ? null : error;
    }

    private async Task SavePreferencesAsync()
    {
        try
        {
            await _store.SaveAsync(PreferencesKey, Preferences);
        }
        catch (SqliteException)
        {
            // Remembering choices is a convenience; the session itself already started.
        }
    }
}
