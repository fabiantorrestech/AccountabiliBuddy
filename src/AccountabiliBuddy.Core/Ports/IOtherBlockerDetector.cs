namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Finds other blockers installed on this computer (Freedom, PluckEye) from their install records only.
/// Cold Turkey has its own <see cref="IColdTurkeyDetector"/>. Nothing of theirs is read beyond that, or changed.
/// Called when asked (Integrations, the setup assistant), never on a timer.
/// </summary>
public interface IOtherBlockerDetector
{
    IReadOnlyList<OtherBlocker> Detect();
}
