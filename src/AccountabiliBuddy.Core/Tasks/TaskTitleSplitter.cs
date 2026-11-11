using System.Text.RegularExpressions;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Finds where a long title (over <see cref="TaskTitleHelp.SoftLimit"/> characters) could be split into a short title and its details,
/// by rules only (no AI, no network; Implementation Notes "Title length, details and project markers"): the first of these breaks
/// found between character 25 and 62, in priority order: brackets or a dash clause, a sentence end, a linking phrase. Nothing found:
/// no suggestion. The user can move the break by a word (<see cref="Moved"/>).
/// </summary>
public static class TaskTitleSplitter
{
    private const int EarliestBreak = 25;
    private const int LatestBreak = 62;

    // In priority order: " (", " - ", " — ", ": ", then ". " and "; ", then linking phrases.
    private static readonly Regex[] Breaks =
    [
        Break(" ("), Break(" - "), Break(" — "), Break(": "), Break(". "), Break("; "),
        Break(" so that "), Break(" because "), Break(" in order to "), Break(" to see "), Break(" which "), Break(" after "),
        Break(" before "), Break(" when "), Break(" in chat "), Break(" with the "),
    ];

    private static readonly Regex EdgeBefore = new(@"[\s(:;,.–—-]+$", RegexOptions.Compiled);
    private static readonly Regex EdgeAfter = new(@"^[\s(:;,.–—-]+", RegexOptions.Compiled);

    /// <summary>The suggested split of <paramref name="title"/>; null when it is no longer than the soft limit or has no break.</summary>
    public static TaskTitleSplit? Split(string title)
    {
        if (title.Length <= TaskTitleHelp.SoftLimit)
        {
            return null;
        }

        string window = title[EarliestBreak..Math.Min(LatestBreak, title.Length)];
        foreach (Regex pattern in Breaks)
        {
            Match found = pattern.Match(window);
            if (found.Success)
            {
                return At(title, EarliestBreak + found.Index);
            }
        }

        return null;
    }

    /// <summary>The split moved a word right (<paramref name="direction"/> 1) or left (-1), as Alt+→ and Alt+← do; the same split at either end.</summary>
    public static TaskTitleSplit Moved(string title, TaskTitleSplit split, int direction)
    {
        int at = direction > 0 ? title.IndexOf(' ', Math.Min(split.At + 1, title.Length)) : split.At > 1 ? title.LastIndexOf(' ', split.At - 1) : -1;
        return at > 0 && at < title.Length - 1 ? At(title, at) : split;
    }

    private static TaskTitleSplit At(string title, int at)
    {
        string head = EdgeBefore.Replace(title[..at], string.Empty);
        string tail = EdgeAfter.Replace(title[at..], string.Empty);
        if (tail.EndsWith(')'))
        {
            tail = tail[..^1];
        }

        return new TaskTitleSplit(head, tail, at);
    }

    private static Regex Break(string text)
    {
        return new Regex(Regex.Escape(text), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
