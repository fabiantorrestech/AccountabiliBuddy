using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Looks for other blockers through <see cref="IBlockerSourceLocator"/> and reads what can be imported. A source
/// that can't be read is still listed, with the problem and a hint to paste its list instead.
/// </summary>
public static class BlockerSourceScanner
{
    private const string PasteHint = "Copy its list from that app and paste it below instead.";

    /// <param name="coldTurkey">Cold Turkey's copied databases, or null if it isn't installed.</param>
    public static IReadOnlyList<DetectedImport> Scan(ColdTurkeyDataFiles? coldTurkey, IReadOnlyList<BlockerExtensionData> extensions)
    {
        var detected = new List<DetectedImport>();
        if (coldTurkey is not null)
        {
            detected.Add(ReadColdTurkey(coldTurkey));
        }

        detected.AddRange(extensions.Select(ReadExtension));
        return detected;
    }

    private static DetectedImport ReadColdTurkey(ColdTurkeyDataFiles files)
    {
        try
        {
            IReadOnlyList<ImportedBlockList> lists = ColdTurkeySettingsReader.ReadDatabase(files.SettingsDatabase);
            int usable = lists.Count(list => !list.IsEmpty);
            int scheduled = lists.Count(list => !list.IsEmpty && list.Schedule.Count > 0);
            return new DetectedImport(ImportSource.ColdTurkey, "Cold Turkey", lists,
                $"{Count(usable, "block")}, {scheduled} with a schedule", null);
        }
        catch (FormatException ex)
        {
            return new DetectedImport(ImportSource.ColdTurkey, "Cold Turkey", Array.Empty<ImportedBlockList>(),
                "Couldn't read its blocks", $"{ex.Message} {PasteHint}");
        }
    }

    private static DetectedImport ReadExtension(BlockerExtensionData data)
    {
        string title = $"{ImportPlanner.SourceLabel(data.Source)} ({data.Where})";
        try
        {
            IReadOnlyDictionary<string, string> storage = LevelDbLogReader.ReadLatestValues(data.StorageLogs);
            IReadOnlyList<ImportedBlockList> lists = data.Source switch
            {
                ImportSource.AppBlock when data.DnrRulesJson is not null => new[] { DnrRulesReader.Read(data.DnrRulesJson, data.Source, title) },
                ImportSource.BlockSite => new[] { BlockSiteStorageReader.Read(storage, title) },
                ImportSource.LeechBlock => LeechBlockOptionsReader.ReadStorage(storage)
                    .Select(list => list with { Name = $"{list.Name} ({data.Where})" }).ToList(),
                _ => Array.Empty<ImportedBlockList>(),
            };

            int sites = lists.Sum(list => list.BlockEntries.Count);
            string? problem = sites > 0 ? null
                : data.HasCompactedStorage ? $"Its list is stored in a form this version can't read. {PasteHint}"
                : $"No blocked sites are saved on this PC (it may keep them in its online account). {PasteHint}";
            string summary = lists.Count > 1 ? $"{Count(lists.Count, "set")}, {Count(sites, "site")}" : Count(sites, "site");
            return new DetectedImport(data.Source, title, lists, summary, problem);
        }
        catch (FormatException ex)
        {
            return new DetectedImport(data.Source, title, Array.Empty<ImportedBlockList>(), "Couldn't read it", $"{ex.Message} {PasteHint}");
        }
    }

    private static string Count(int count, string noun)
    {
        return count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }
}
