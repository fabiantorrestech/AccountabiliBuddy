using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Project markers at the start of a title (Implementation Notes "Project markers"), rule-based: "Name - ", "Name: ", "[Name]",
/// "(Name)", "{Name}", "&lt;Name&gt;", "⌜Name⌟" and "Name 123 - " (which also gives the trailing word as a tag), plus the markers the user
/// taught. A known label is matched first; otherwise the marker suggests a tag.
/// </summary>
public static class TaskProjectMarkers
{
    private const int MaxName = 16;

    // ECMAScript so \w means letters, digits and underscore as in the prototype this was settled in, not every Unicode letter.
    private static readonly Regex BuiltIn = new(
        @"^\s*(?:\[([^\]]{2,16})\]|\(([^)]{2,16})\)|\{([^}]{2,16})\}|<([^>]{2,16})>|⌜([^⌟]{2,16})⌟|([A-Za-z][\w-]{1,10}(?: [A-Z0-9][\w-]{0,6})?)\s*[-:–—])\s+",
        RegexOptions.ECMAScript | RegexOptions.Compiled);

    /// <summary>The marker at the start of <paramref name="title"/>; null when there is none.</summary>
    public static TaskProjectMarker? Find(string title, IReadOnlyList<TaskLabel> labels, IReadOnlyList<ProjectMarkerPattern>? custom = null)
    {
        Match found = BuiltIn.Match(title);
        if (found.Success)
        {
            string raw = found.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value.Trim();
            return Marker(found.Value, raw, title, labels);
        }

        foreach (ProjectMarkerPattern pattern in custom ?? [])
        {
            Match own = Custom(pattern).Match(title);
            if (own.Success)
            {
                return Marker(own.Value, own.Groups[1].Value.Trim(), title, labels);
            }
        }

        return null;
    }

    private static TaskProjectMarker Marker(string full, string raw, string title, IReadOnlyList<TaskLabel> labels)
    {
        string[] words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string first = words.Length > 0 ? words[0] : raw;
        TaskLabel? label = labels.FirstOrDefault(candidate => candidate.IsLive && string.Equals(candidate.Name, first, StringComparison.OrdinalIgnoreCase));
        string? tag = words.Length > 1 ? words[1] : label is null ? first : null;
        return new TaskProjectMarker(full, raw, label?.Id, tag, title[full.Length..]);
    }

    // The user's own marker as a pattern: Open + name + Close, or a word + Close when there is no Open.
    private static Regex Custom(ProjectMarkerPattern pattern)
    {
        string close = Regex.Escape(pattern.Close);
        string text = pattern.Open.Length > 0
            ? @"^\s*" + Regex.Escape(pattern.Open) + @"([^" + close + @"]{2," + MaxName + "})" + close + @"\s+"
            : @"^\s*(\w[\w-]{1," + (MaxName - 1) + @"})\s*" + close + @"\s+";
        return new Regex(text, RegexOptions.CultureInvariant);
    }
}
