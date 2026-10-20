namespace AccountabiliBuddy.Core.Import;

/// <summary>Every Cold Turkey block as its settings say right now. While Cold Turkey is paused, no block blocks.</summary>
public sealed record ColdTurkeyBlocks(bool IsPaused, IReadOnlyList<ColdTurkeyBlockState> Blocks)
{
    /// <summary>The names of the blocks blocking at <paramref name="localTime"/>.</summary>
    public IReadOnlySet<string> OnAt(DateTime localTime)
    {
        return IsPaused
            ? new HashSet<string>(StringComparer.Ordinal)
            : Blocks.Where(block => block.IsOnAt(localTime)).Select(block => block.Name).ToHashSet(StringComparer.Ordinal);
    }
}
