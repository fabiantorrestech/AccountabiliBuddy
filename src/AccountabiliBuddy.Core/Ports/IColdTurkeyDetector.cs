namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Detects a Cold Turkey Blocker install from public signs only (install folder, file version, running processes,
/// registered browser hosts). It never reads Cold Turkey's settings; which blocks are on comes from
/// <see cref="IColdTurkeyBlockReader"/>.
/// </summary>
public interface IColdTurkeyDetector
{
    ColdTurkeyStatus Detect();
}
