namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Detects a Cold Turkey Blocker install from public signs only (install folder, file version, running processes,
/// registered browser hosts). Cold Turkey's own settings are never read: they are stored in a private format.
/// </summary>
public interface IColdTurkeyDetector
{
    ColdTurkeyStatus Detect();
}
