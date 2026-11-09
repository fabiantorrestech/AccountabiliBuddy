namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A task's labels (ADR 72): plain names, no colour, several per task, cutting across categories. Compared case-insensitively
/// ("Home" = "home"), shown as the first spelling seen. In a typed title, <c>##word</c> adds the label "word" and leaves the
/// title; a single <c>#</c> stays text ("#1 priority"). The phone's twin is <c>TaskLabels</c> in android/shared; both give
/// the same result.
/// </summary>
public static class TaskLabels
{
    /// <summary>The most labels one task keeps; more are dropped.</summary>
    public const int MaxPerTask = 10;

    /// <summary>The longest label kept; a longer one is cut.</summary>
    public const int MaxLength = 40;

    /// <summary>The marker that starts a label in a typed title.</summary>
    public const string Marker = "##";

    /// <summary>
    /// The labels cleaned: trimmed, empty ones dropped, cut to <see cref="MaxLength"/>, case-insensitive duplicates dropped
    /// (the first spelling kept), at most <see cref="MaxPerTask"/>. Null or none gives null, so a task without labels writes
    /// nothing and breaks ties as it did before labels existed.
    /// </summary>
    public static IReadOnlyList<string>? Clean(IEnumerable<string>? labels)
    {
        if (labels is null)
        {
            return null;
        }

        var kept = new List<string>();
        foreach (string raw in labels)
        {
            string label = (raw ?? string.Empty).Trim();
            if (label.Length > MaxLength)
            {
                label = label[..MaxLength].TrimEnd();
            }

            if (label.Length == 0 || kept.Any(other => Same(other, label)))
            {
                continue;
            }

            kept.Add(label);
            if (kept.Count == MaxPerTask)
            {
                break;
            }
        }

        return kept.Count == 0 ? null : kept;
    }

    /// <summary>Whether two labels are the same label (case-insensitive).</summary>
    public static bool Same(string first, string second)
    {
        return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether <paramref name="labels"/> holds <paramref name="label"/>.</summary>
    public static bool Has(IReadOnlyList<string>? labels, string label)
    {
        return labels is not null && labels.Any(other => Same(other, label));
    }

    /// <summary>
    /// The labels merged: <paramref name="existing"/> first, then <paramref name="added"/>, cleaned as <see cref="Clean"/> does.
    /// </summary>
    public static IReadOnlyList<string>? With(IReadOnlyList<string>? existing, IEnumerable<string> added)
    {
        return Clean((existing ?? []).Concat(added));
    }

    /// <summary>The labels without <paramref name="label"/>; null when none are left.</summary>
    public static IReadOnlyList<string>? Without(IReadOnlyList<string>? labels, string label)
    {
        return Clean((labels ?? []).Where(other => !Same(other, label)));
    }

    /// <summary>
    /// The labels with <paramref name="from"/> renamed to <paramref name="to"/> (a blank <paramref name="to"/> deletes it); the
    /// same list when it doesn't have <paramref name="from"/>.
    /// </summary>
    public static IReadOnlyList<string>? Renamed(IReadOnlyList<string>? labels, string from, string to)
    {
        if (!Has(labels, from))
        {
            return labels;
        }

        return Clean(labels!.Select(label => Same(label, from) ? to : label));
    }

    /// <summary>
    /// A typed title split into its text and its <c>##</c> labels: "Fix printer ##home" gives ("Fix printer", ["home"]). A label
    /// runs to the next space; a <c>##</c> with nothing after it stays text, and so does a single <c>#</c>. The text's spaces are
    /// tidied where a label was taken out.
    /// </summary>
    public static (string Title, IReadOnlyList<string>? Labels) Parse(string typed)
    {
        if (string.IsNullOrEmpty(typed) || !typed.Contains(Marker, StringComparison.Ordinal))
        {
            return (typed ?? string.Empty, null);
        }

        var labels = new List<string>();
        var words = new List<string>();
        foreach (string word in typed.Split(' '))
        {
            if (word.StartsWith(Marker, StringComparison.Ordinal) && word.Length > Marker.Length && word[Marker.Length] != '#')
            {
                labels.Add(word[Marker.Length..]);
            }
            else
            {
                words.Add(word);
            }
        }

        string title = string.Join(' ', words.Where(word => word.Length > 0)).Trim();
        return (title, Clean(labels));
    }

    /// <summary>The labels as one string for an item's tie-break; empty for none.</summary>
    public static string TieText(IReadOnlyList<string>? labels)
    {
        return labels is null ? string.Empty : string.Join("\u0004", labels);
    }

    /// <summary>Every label the items carry, once each (case-insensitive, the first spelling seen), sorted by name.</summary>
    public static IReadOnlyList<string> AllOf(IEnumerable<IReadOnlyList<string>?> labelLists)
    {
        var all = new List<string>();
        foreach (string label in labelLists.Where(list => list is not null).SelectMany(list => list!))
        {
            if (!all.Any(other => Same(other, label)))
            {
                all.Add(label);
            }
        }

        return all.OrderBy(label => label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
