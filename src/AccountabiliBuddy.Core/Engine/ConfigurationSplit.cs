using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>The result of <see cref="ConfigurationChangeGuard.Split"/>.</summary>
/// <param name="ApplyNow">The current configuration plus every part of the change that is stricter or neutral.</param>
/// <param name="WaitingChanges">What still differs from the proposed configuration because it is looser; empty when
/// the whole change applied.</param>
public sealed record ConfigurationSplit(ProfileDocument ApplyNow, IReadOnlyList<string> WaitingChanges)
{
    public bool HasWaitingChanges => WaitingChanges.Count > 0;
}
