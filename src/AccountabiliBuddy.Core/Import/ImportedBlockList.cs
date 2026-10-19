using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Import;

/// <summary>One block list read from another blocker, before it becomes a profile (see <see cref="ImportPlanner"/>).</summary>
/// <param name="Name">The list's name in the other blocker, e.g. a Cold Turkey block name.</param>
/// <param name="BlockEntries">Sites and apps to block, in the paste-list syntax of <see cref="Engine.BlockListParser"/>.</param>
/// <param name="AllowEntries">Exceptions: sites and apps to allow.</param>
/// <param name="Schedule">When the list blocks each week; empty if it is started by hand.</param>
/// <param name="UnlockOptions">Ways to lift the list taken from the other blocker: null when it says nothing (the app's
/// default applies), empty when it allows none.</param>
/// <param name="Notes">What could not be carried over, shown before importing.</param>
public sealed record ImportedBlockList(
    ImportSource Source,
    string Name,
    IReadOnlyList<string> BlockEntries,
    IReadOnlyList<string> AllowEntries,
    IReadOnlyList<WeekRange> Schedule,
    IReadOnlyList<UnlockOption>? UnlockOptions,
    IReadOnlyList<string> Notes)
{
    public bool IsEmpty => BlockEntries.Count == 0 && AllowEntries.Count == 0;
}
