namespace AccountabiliBuddy.Core.Goals;

/// <summary>Guesses the kind of a typed entry; the user can change it afterwards. Twin: Kotlin <c>GoalAppMatchGuess</c>.</summary>
public static class GoalAppMatchGuess
{
    private static readonly HashSet<string> KnownTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "net", "org", "io", "app", "dev", "tv", "gg", "co", "me", "ai", "edu", "gov",
    };

    /// <summary>
    /// Space → Name; ends ".exe" → Process; exactly two dotted labels with a plausible top-level domain → Website; other dotted
    /// text → Package; anything else → Name. Trimmed; empty → null.
    /// </summary>
    public static GoalAppMatch? Guess(string? text)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Any(char.IsWhiteSpace))
        {
            return new GoalAppMatch(GoalAppMatchKind.Name, trimmed);
        }

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new GoalAppMatch(GoalAppMatchKind.Process, trimmed);
        }

        if (!trimmed.Contains('.'))
        {
            return new GoalAppMatch(GoalAppMatchKind.Name, trimmed);
        }

        string[] labels = trimmed.Split('.');
        bool website = labels.Length == 2 && labels[0].Length > 0 && IsPlausibleTld(labels[1]);
        return new GoalAppMatch(website ? GoalAppMatchKind.Website : GoalAppMatchKind.Package, trimmed);
    }

    private static bool IsPlausibleTld(string label)
    {
        if (label.Length is < 2 or > 6 || !label.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
        {
            return false;
        }

        return label.Length == 2 || KnownTlds.Contains(label);
    }
}
