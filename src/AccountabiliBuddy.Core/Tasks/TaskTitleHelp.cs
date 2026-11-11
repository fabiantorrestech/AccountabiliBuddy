using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The add box's title help (ADR 76, spec 05 section 6), rule-based and local: the 60-character soft limit with a counter from 50,
/// the project marker and the split suggestion. None of it blocks adding a task.
/// </summary>
public static partial class TaskTitleHelp
{
    public const int SoftLimit = 60;
    public const int CounterFrom = 50;

    [GeneratedRegex(@"(^|\s)@([\w-]+)")]
    private static partial Regex LabelWord();

    [GeneratedRegex(@"(^|\s)#([\w-]+)")]
    private static partial Regex TagWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>
    /// Typed text as the add box reads it: "@work" names the label when one has that name (any other "@word" stays text), "#home" is a
    /// tag; both leave the title, whose spaces are tidied.
    /// </summary>
    public static TaskTypedTitle Parse(string text, IReadOnlyList<TaskLabel> labels)
    {
        Guid? label = null;
        string rest = LabelWord().Replace(text, match =>
        {
            TaskLabel? named = labels.FirstOrDefault(candidate => candidate.IsLive && string.Equals(candidate.Name, match.Groups[2].Value, StringComparison.OrdinalIgnoreCase));
            if (named is null)
            {
                return match.Value;
            }

            label ??= named.Id;
            return match.Groups[1].Value;
        });
        var tags = new List<string>();
        rest = TagWord().Replace(rest, match =>
        {
            tags.Add(match.Groups[2].Value);
            return match.Groups[1].Value;
        });
        return new TaskTypedTitle(Spaces().Replace(rest, " ").Trim(), label, TaskLabels.Clean(tags) ?? []);
    }

    /// <summary>
    /// The help for a title (without its "@label" and "#tag" words): the counter, the project marker at its start and the split
    /// suggestion. <paramref name="keepAsTyped"/> ("Keep as typed") stops both suggestions.
    /// </summary>
    public static TaskTitleAnalysis Analyze(string title, IReadOnlyList<TaskLabel> labels, IReadOnlyList<ProjectMarkerPattern>? customMarkers = null, bool keepAsTyped = false)
    {
        TaskProjectMarker? marker = keepAsTyped ? null : TaskProjectMarkers.Find(title, labels, customMarkers);
        TaskTitleSplit? split = keepAsTyped ? null : TaskTitleSplitter.Split(title);
        return new TaskTitleAnalysis(title.Length, title.Length >= CounterFrom, title.Length > SoftLimit, marker, split);
    }
}
