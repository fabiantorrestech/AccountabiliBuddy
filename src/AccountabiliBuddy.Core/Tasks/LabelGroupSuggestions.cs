using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The label groups the setup assistant suggests on upgrade (ADR 93): users already fake categories with name prefixes ("Work - AI",
/// "AB-Improvements"). A prefix (before the first hyphen, trimmed, compared case-insensitively) that two or more live, uncategorised labels
/// share is a suggestion; nothing is grouped until the user ticks it. Each label also gets the shorter name "Shorten the names" (opt-in) would
/// give it; one that would clash with another live label or another short name is marked skipped (never renamed).
/// </summary>
public static partial class LabelGroupSuggestions
{
    // "Work - AI": group 1 the prefix (no hyphen in it), group 2 the rest.
    [GeneratedRegex(@"^\s*([^-]+?)\s*-\s*(.+)$")]
    private static partial Regex PrefixPattern();

    /// <param name="labels">Every label (tombstones and hidden ones too: a short name must not clash with any live label).</param>
    /// <param name="groups">The label groups, to tell an uncategorised label and to find a group that already has a suggested name.</param>
    /// <param name="shown">ADR 89: whether this device shows a label; only shown labels are suggested. Null: every label.</param>
    /// <returns>The suggestions, in the order of each one's first label.</returns>
    public static IReadOnlyList<LabelGroupSuggestion> For(IReadOnlyList<TaskLabel> labels, IReadOnlyList<SyncTaskLabelGroup>? groups, Func<TaskLabel, bool>? shown = null)
    {
        IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(TaskLabelGroupBook.Live(groups));
        List<TaskLabel> live = labels.Where(label => label.IsLive && label.Name.Trim().Length > 0).ToList();
        List<(TaskLabel Label, string Prefix, string ShortName)> candidates = live
            .Where(label => (shown is null || shown(label)) && TaskLabelGroupBook.GroupOf(label, liveGroups) is null)
            .OrderBy(label => label.Position).ThenBy(label => label.Id.ToString("D"), StringComparer.Ordinal)
            .Select(label => (Label: label, Match: PrefixPattern().Match(label.Name)))
            .Where(entry => entry.Match.Success && entry.Match.Groups[2].Value.Trim().Length > 0)
            .Select(entry => (entry.Label, Prefix: entry.Match.Groups[1].Value.Trim(), ShortName: entry.Match.Groups[2].Value.Trim()))
            .ToList();

        List<List<(TaskLabel Label, string Prefix, string ShortName)>> shared = candidates
            .GroupBy(entry => entry.Prefix, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .Select(group => group.ToList())
            .ToList();

        // Short names given more than once (case-insensitive) clash with each other.
        HashSet<string> repeated = shared.SelectMany(group => group)
            .GroupBy(entry => entry.ShortName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return shared.Select(group => new LabelGroupSuggestion(
                group[0].Prefix,
                TaskLabelGroupBook.Named(groups, group[0].Prefix)?.Id,
                group.Select(entry => Item(entry.Label, entry.ShortName, live, repeated)).ToList()))
            .ToList();
    }

    private static LabelGroupSuggestionItem Item(TaskLabel label, string shortName, List<TaskLabel> live, HashSet<string> repeated)
    {
        if (live.Any(other => other.Id != label.Id && TaskLabel.SameName(other.Name, shortName)))
        {
            return new LabelGroupSuggestionItem(label.Id, label.Name, shortName, true, $"Keeps its name: \"{shortName}\" is already a label.");
        }

        if (repeated.Contains(shortName))
        {
            return new LabelGroupSuggestionItem(label.Id, label.Name, shortName, true, $"Keeps its name: another label would also become \"{shortName}\".");
        }

        return new LabelGroupSuggestionItem(label.Id, label.Name, shortName, false, null);
    }
}
