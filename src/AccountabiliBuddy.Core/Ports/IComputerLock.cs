namespace AccountabiliBuddy.Core.Ports;

/// <summary>Locks the computer (back to the sign-in screen; programs and downloads keep running) or puts it to sleep.</summary>
public interface IComputerLock
{
    /// <returns>False when the system refused.</returns>
    bool Lock();

    /// <returns>False when the system refused.</returns>
    bool Sleep();
}
