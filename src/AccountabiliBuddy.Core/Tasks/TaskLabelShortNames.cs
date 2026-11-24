using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The name a label shows under its own category heading (Tasks redesign, checklist 3.56): "Work - AI" under Work reads "AI". The prefix is the
/// part before the first hyphen (the same rule as <see cref="LabelGroupSuggestions"/>); it is dropped only when it is the category's name, so a
/// label elsewhere, or one whose prefix is something else, keeps its full name. Display only: the label itself is never renamed.
/// </summary>
public static partial class TaskLabelShortNames
{
    // "Work - AI": group 1 the prefix (no hyphen in it), group 2 the rest.
    [GeneratedRegex(@"^\s*([^-]+?)\s*-\s*(.+)$")]
    private static partial Regex PrefixPattern();

    /// <summary>
    /// <paramref name="labelName"/> without its "<paramref name="categoryName"/> - " prefix (compared like label names: trimmed, any case);
    /// the full name when there is no category, no prefix, or another prefix.
    /// </summary>
    public static string Of(string labelName, string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return labelName;
        }

        Match match = PrefixPattern().Match(labelName);
        return match.Success && TaskLabel.SameName(match.Groups[1].Value, categoryName) ? match.Groups[2].Value.Trim() : labelName;
    }
}
