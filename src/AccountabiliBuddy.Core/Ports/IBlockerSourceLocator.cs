namespace AccountabiliBuddy.Core.Ports;

/// <summary>Finds other blockers' data on this computer for the import. Everything is read-only.</summary>
public interface IBlockerSourceLocator
{
    /// <returns>Copies of Cold Turkey's databases, or null if Cold Turkey's data isn't on this computer.</returns>
    ColdTurkeyDataFiles? CopyColdTurkeyData();

    /// <summary>AppBlock, BlockSite and LeechBlock extensions in the browsers' profiles.</summary>
    IReadOnlyList<BlockerExtensionData> FindBlockerExtensions();
}
