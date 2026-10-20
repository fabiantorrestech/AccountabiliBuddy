namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// Where a search shows up in a text: every place it occurs, ignoring case, without overlaps. Ordinal, so a match is
/// always exactly the typed characters (no culture rules that make "ß" match "ss" with a different length).
/// </summary>
public static class TextMatcher
{
    /// <param name="query">What was typed; spaces around it are ignored, and a blank query finds nothing.</param>
    /// <returns>Each occurrence as a start index and length, in order.</returns>
    public static IReadOnlyList<(int Start, int Length)> Find(string? text, string query)
    {
        string wanted = query.Trim();
        var matches = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(text) || wanted.Length == 0)
        {
            return matches;
        }

        int from = 0;
        while (from <= text.Length - wanted.Length)
        {
            int at = text.IndexOf(wanted, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                break;
            }

            matches.Add((at, wanted.Length));
            from = at + wanted.Length;
        }

        return matches;
    }
}
