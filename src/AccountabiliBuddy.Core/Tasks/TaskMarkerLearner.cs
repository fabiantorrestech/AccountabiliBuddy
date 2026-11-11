using System.Text.RegularExpressions;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Learning custom project markers (Implementation Notes): when <see cref="MinTitles"/> or more titles made in the last
/// <see cref="Days"/> days start with the same pair of non-alphanumeric characters around a name ("⟦Name⟧"), or the same
/// non-alphanumeric suffix after a word ("Name ›"), that style is offered once. The built-in styles (<see cref="TaskProjectMarkers"/>)
/// and markers already taught are never offered. Rule-based and local; the result holds only the characters, never a title.
/// </summary>
public static partial class TaskMarkerLearner
{
    public const int MinTitles = 3;
    public const int Days = 30;

    // Characters the built-in markers already use.
    private const string BuiltInOpeners = "[({<⌜";
    private const string BuiltInSuffixes = "-:–—";

    [GeneratedRegex(@"^\s*([^\p{L}\p{N}\s])\p{L}[^\r\n]{1,14}?([^\p{L}\p{N}\s])\s")]
    private static partial Regex Paired();

    [GeneratedRegex(@"^\s*\p{L}[\p{L}\p{N}-]{1,15}\s*([^\p{L}\p{N}\s])\s")]
    private static partial Regex Suffixed();

    /// <summary>The styles that qualify, most used first.</summary>
    public static IReadOnlyList<ProjectMarkerCandidate> Candidates(
        IEnumerable<(string Title, DateTimeOffset CreatedAt)> titles, IReadOnlyList<ProjectMarkerPattern> known, DateTimeOffset now)
    {
        var counts = new Dictionary<ProjectMarkerPattern, int>();
        foreach ((string title, DateTimeOffset createdAt) in titles)
        {
            if (now - createdAt > TimeSpan.FromDays(Days))
            {
                continue;
            }

            if (StyleOf(title) is ProjectMarkerPattern style && !known.Contains(style))
            {
                counts[style] = counts.GetValueOrDefault(style) + 1;
            }
        }

        return counts.Where(pair => pair.Value >= MinTitles)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.Open, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Close, StringComparer.Ordinal)
            .Select(pair => new ProjectMarkerCandidate(pair.Key, pair.Value))
            .ToList();
    }

    private static ProjectMarkerPattern? StyleOf(string title)
    {
        Match paired = Paired().Match(title);
        if (paired.Success)
        {
            string open = paired.Groups[1].Value;
            string close = paired.Groups[2].Value;
            return BuiltInOpeners.Contains(open, StringComparison.Ordinal) ? null : new ProjectMarkerPattern(open, close);
        }

        Match suffixed = Suffixed().Match(title);
        if (suffixed.Success)
        {
            string close = suffixed.Groups[1].Value;
            return BuiltInSuffixes.Contains(close, StringComparison.Ordinal) ? null : new ProjectMarkerPattern(string.Empty, close);
        }

        return null;
    }
}
