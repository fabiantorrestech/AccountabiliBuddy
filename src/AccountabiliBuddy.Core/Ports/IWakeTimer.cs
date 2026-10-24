namespace AccountabiliBuddy.Core.Ports;

/// <summary>Wakes the computer from sleep at a time, so an alarm can ring (ADR 35).</summary>
public interface IWakeTimer
{
    /// <summary>Wakes the computer at <paramref name="at"/> (replacing any earlier time); false when it can't be set.</summary>
    bool WakeAt(DateTimeOffset at);

    /// <summary>Cancels the wake-up set before.</summary>
    void Cancel();

    /// <summary>Why the computer may not wake (e.g. the power plan doesn't allow wake timers on battery); null when it should.</summary>
    string? Problem();
}
