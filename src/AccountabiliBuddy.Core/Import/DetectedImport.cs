namespace AccountabiliBuddy.Core.Import;

/// <summary>One blocker found on this computer and what can be imported from it.</summary>
/// <param name="Title">E.g. "Cold Turkey" or "AppBlock (Edge)".</param>
/// <param name="Summary">E.g. "5 blocks, 2 with a schedule".</param>
/// <param name="Problem">Why nothing (or not everything) could be read, with what to do instead; null when fine.</param>
public sealed record DetectedImport(ImportSource Source, string Title, IReadOnlyList<ImportedBlockList> Lists, string Summary, string? Problem)
{
    public bool HasLists => Lists.Any(list => !list.IsEmpty);
}
