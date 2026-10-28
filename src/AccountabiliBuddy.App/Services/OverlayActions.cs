namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the overlay and its panel can ask the app to do, and what they can ask it about, set once by the app. Every action goes
/// through the same commands and gates the Today page uses (no escape hatch); the overlay adds none of its own.
/// </summary>
public sealed class OverlayActions
{
    /// <summary>"Done": ticks the task in front; with no tasks, the app's end flow (with its wait and reason).</summary>
    public Func<Task> EndSession { get; set; } = () => Task.CompletedTask;

    /// <summary>"+10 min" (the engine's ExtendFocus); false when it can't work now.</summary>
    public Func<bool> ExtendFocus { get; set; } = () => false;

    /// <summary>Opens the main window.</summary>
    public Action OpenApp { get; set; } = () => { };

    /// <summary>Opens Settings at Alerts &amp; on-screen.</summary>
    public Action OpenSettings { get; set; } = () => { };

    /// <summary>Opens the capture box (Ctrl+Alt+N) to park a thought.</summary>
    public Action ParkThought { get; set; } = () => { };

    /// <summary>Opens the quick session card to start a session.</summary>
    public Action ShowStart { get; set; } = () => { };

    /// <summary>Starts a session with the default profile; false when it can't start.</summary>
    public Func<Task<bool>> StartDefault { get; set; } = () => Task.FromResult(false);

    /// <summary>Lunch can start now.</summary>
    public Func<bool> CanStartLunch { get; set; } = () => false;

    /// <summary>Starts lunch.</summary>
    public Func<Task> StartLunch { get; set; } = () => Task.CompletedTask;

    /// <summary>The next calendar event starting within the next 10 minutes (title, start), or null.</summary>
    public Func<(string Title, DateTime LocalStart)?> NextMeeting { get; set; } = () => null;

    /// <summary>Today's deep work goal is met.</summary>
    public Func<bool> IsWorkDone { get; set; } = () => false;

    /// <summary>"Applies at 1:48 PM": when a looser change that is waiting takes effect; null when none waits.</summary>
    public Func<DateTimeOffset?> PendingChangeAt { get; set; } = () => null;

    /// <summary>The tray's ⌃ (when the overlay covers the tray): opens Windows' tray overflow.</summary>
    public Action OpenTrayOverflow { get; set; } = () => { };

    /// <summary>Windows' calendar flyout (a click on the overlay's own clock).</summary>
    public Action OpenCalendarFlyout { get; set; } = () => { };

    /// <summary>The open come-backs, newest first (the large panel's "Come back to").</summary>
    public Func<IReadOnlyList<string>> ComebackTitles { get; set; } = () => [];

    /// <summary>The next blocks of the day as (time, text) pairs (the large panel's "Next up").</summary>
    public Func<IReadOnlyList<(string Time, string Text)>> NextUp { get; set; } = () => [];

    /// <summary>Today's sync QR codes for the phone (ADR 22), while the QR code method is on; null hides the panel's QR button.</summary>
    public Action? ShowSyncQr { get; set; }

    /// <summary>Other computers' and the watch's batteries: none yet (their sync data carries no level; ADR 52 would add one).</summary>
    public Func<IReadOnlyList<(string DeviceId, string Name, int Percent, string Kind)>> OtherBatteries { get; set; } = () => [];
}
