namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Tells when Ctrl and Alt are both held, whichever app is in front, from the keyboard's own events (no polling). The overlay panel peeks
/// while they stay held. On layouts where AltGr counts as Ctrl+Alt this also fires for AltGr: the peek can be turned off for that.
/// </summary>
public interface ICtrlAltWatcher
{
    /// <summary>True while both keys are down.</summary>
    bool IsHeld { get; }

    /// <summary>Raised with the new state when it changes. Raised on the watcher's own thread.</summary>
    event EventHandler<bool>? HeldChanged;

    /// <summary>Reads Ctrl and Alt from the keyboard state again, for when a key-up was missed (the secure desktop, a lock screen, an elevated window in front).</summary>
    void Resync();

    /// <summary>Starts or stops listening (no hook is installed while the peek is off).</summary>
    void SetListening(bool listening);
}
