using AccountabiliBuddy.Core.Ports;
using Windows.Win32;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>LockWorkStation, and sleep (not hibernate) through SetSuspendState.</summary>
public sealed class Win32ComputerLock : IComputerLock
{
    public bool Lock() => PInvoke.LockWorkStation();

    public bool Sleep() => PInvoke.SetSuspendState(false, false, false);
}
