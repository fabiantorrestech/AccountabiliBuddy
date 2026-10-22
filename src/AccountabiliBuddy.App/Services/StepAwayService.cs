using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Step away during a session: the countdown pauses (<see cref="SessionController.StepAway"/>) until the user is back,
/// which is any keyboard or mouse input, the shortcut again, or "I'm back". It never shortens a wait or ends the
/// session. Optionally steps away by itself after a few idle minutes (off by default). Use from the UI thread only
/// (the shortcut is marshalled here).
/// </summary>
public sealed class StepAwayService : IDisposable
{
    private const string DocumentKey = "stepAway";

    // The click or key press that stepped away lands just after it; input in this window isn't "back".
    private static readonly TimeSpan PressGrace = TimeSpan.FromSeconds(2);

    private readonly SessionController _sessions;
    private readonly IUserIdleDetector _idle;
    private readonly IGlobalHotkey _hotkey;
    private readonly SqliteJsonDocumentStore _store;
    private readonly TimeProvider _time;

    // Input from before the app started (a session resumed while away) isn't "back".
    private readonly DateTimeOffset _startedAt;

    public StepAwayService(SessionController sessions, IUserIdleDetector idle, IGlobalHotkey hotkey, SqliteJsonDocumentStore store, TimeProvider time)
    {
        _sessions = sessions;
        _idle = idle;
        _hotkey = hotkey;
        _store = store;
        _time = time;
        _startedAt = time.GetUtcNow();

        _hotkey.Pressed += (_, _) => Dispatcher.UIThread.Post(Toggle);
        _sessions.Ticked += OnTicked;
        _sessions.StateChanged += OnStateChanged;
    }

    public StepAwaySettings Settings { get; private set; } = StepAwaySettings.Default;

    /// <summary>Why the shortcut isn't active, or null when it is (or none is set).</summary>
    public string? HotkeyError { get; private set; }

    public bool IsAway => _sessions.State.IsAway;

    /// <summary>"Away · 4:12" while away, else empty.</summary>
    public string AwayText => _sessions.State.AwaySince is DateTimeOffset since ? $"Away · {Clock(_time.GetUtcNow() - since)}" : string.Empty;

    /// <summary>Raised when the user steps away or is back, and every second while away (for <see cref="AwayText"/>).</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            StepAwaySettings? loaded = Task.Run(() => _store.LoadAsync<StepAwaySettings>(DocumentKey)).GetAwaiter().GetResult();
            Settings = loaded is null ? StepAwaySettings.Default : Clamped(loaded);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Settings = StepAwaySettings.Default;
        }

        RegisterHotkey(Settings.Hotkey);
    }

    /// <returns>False with no session running or when already away.</returns>
    public bool StepAway(AwayKind kind)
    {
        return _sessions.StepAway(kind);
    }

    public void Return()
    {
        _sessions.Return();
    }

    /// <summary>The shortcut: steps away during a session, or is "I'm back" while away.</summary>
    public void Toggle()
    {
        if (IsAway)
        {
            Return();
        }
        else
        {
            StepAway(AwayKind.Break);
        }
    }

    /// <summary>Ends the away on input after it started; steps away after the idle minutes when that's on. Runs every second.</summary>
    public void CheckNow()
    {
        SessionState state = _sessions.State;
        if (!state.IsActive)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        TimeSpan idleFor = _idle.GetIdleTime();
        DateTimeOffset lastInput = now - idleFor;

        if (state.AwaySince is DateTimeOffset since)
        {
            DateTimeOffset ignoreUntil = (since > _startedAt ? since : _startedAt) + PressGrace;
            if (lastInput > ignoreUntil)
            {
                Return();
            }

            return;
        }

        if (Settings.AutoAfterIdle && idleFor >= TimeSpan.FromMinutes(Settings.IdleMinutes))
        {
            // From now, not from the last input: the minutes before count as focus.
            StepAway(AwayKind.Break);
        }
    }

    /// <returns>Null on success, otherwise why the shortcut could not be used (the previous one stays active).</returns>
    public async Task<string?> ChangeHotkeyAsync(string text)
    {
        if (text.Trim().Length == 0)
        {
            _hotkey.Unregister();
            HotkeyError = null;
            await SaveAsync(Settings with { Hotkey = string.Empty });
            return null;
        }

        if (!HotkeyChord.TryParse(text, out HotkeyChord? chord, out string? parseError))
        {
            return parseError;
        }

        if (!_hotkey.TryRegister(chord, out string? registerError))
        {
            RegisterHotkey(Settings.Hotkey); // Put the old one back.
            return registerError;
        }

        HotkeyError = null;
        await SaveAsync(Settings with { Hotkey = chord.ToString() });
        return null;
    }

    public async Task SaveAsync(StepAwaySettings settings)
    {
        Settings = Clamped(settings);
        await _store.SaveAsync(DocumentKey, Settings);
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
        _sessions.StateChanged -= OnStateChanged;
        _hotkey.Unregister();
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
        if (IsAway)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterHotkey(string text)
    {
        if (text.Trim().Length == 0)
        {
            HotkeyError = null;
            return;
        }

        if (!HotkeyChord.TryParse(text, out HotkeyChord? chord, out string? parseError))
        {
            HotkeyError = parseError;
            return;
        }

        HotkeyError = _hotkey.TryRegister(chord, out string? error) ? null : error;
    }

    private static StepAwaySettings Clamped(StepAwaySettings settings)
    {
        return settings with
        {
            Hotkey = settings.Hotkey ?? StepAwaySettings.Default.Hotkey,
            IdleMinutes = Math.Clamp(settings.IdleMinutes, 1, 60),
        };
    }

    private static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes}:{span.Seconds:00}";
    }
}
