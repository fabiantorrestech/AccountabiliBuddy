namespace AccountabiliBuddy.Core.Ports;

/// <summary>The process that brings the app back if it is killed while the quit safeguard holds (ADR 59).</summary>
public interface IQuitWatchdog
{
    /// <summary>Keeps it running, restarting it if it was killed, and tells it whether the safeguard holds now.</summary>
    void Keep(bool locked);

    /// <summary>The app is ending on purpose, or the safeguard went off: it exits and brings nothing back.</summary>
    void StandDown();
}
