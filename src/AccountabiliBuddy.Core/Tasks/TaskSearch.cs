using System.Text;
using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Search over the task list (ADR 76, spec 03 section A): a number ("142") finds the task, a number with letters ("142b") its step;
/// anything else is read as words: "@label" (the label's name starts with it, no case; an underscore stands for a space, "@deep_w"
/// finds "Deep work"; the Add box's word rule), "#tag" (a tag starts with it) and plain words (each found, in any order, in the
/// title, a step, the note or a tag). Every word and token must match (they AND); an "@label" no label starts with finds nothing;
/// a lone "@" or "#" is ignored. A query that looks like a number ("142", "142b"; ASCII digits only) is looked up by number first (a
/// task's former temporary numbers too); when no task or step has that number ("10am", "3pm", "1st", "2fa", "4k") it is a word.
/// Label and tag words are letters, marks, digits, connector punctuation and hyphens in any script. Case folding is one simple
/// lower-case mapping per character (<see cref="Fold"/>), the same on the phone (TaskSearch.kt). Pure; the text is the user's, so nothing here logs.
/// </summary>
public static partial class TaskSearch
{
    /// <summary>The hint in the search boxes.</summary>
    public const string Hint = "Search · @label #tag";

    [GeneratedRegex(@"^[0-9]+[a-z]{0,4}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^@([\p{L}\p{M}\p{Nd}\p{Pc}-]+)$")]
    private static partial Regex LabelToken();

    [GeneratedRegex(@"^#([\p{L}\p{M}\p{Nd}\p{Pc}-]+)$")]
    private static partial Regex TagToken();

    /// <summary>The query looks like a task ID ("142", "142b", case and outer spaces ignored): <see cref="Find"/> tries the number first.</summary>
    public static bool IsId(string query)
    {
        return IdPattern().IsMatch(Fold(query.Trim()));
    }

    /// <summary>
    /// The case folding of every comparison here, the same on the phone: each character through its simple lower-case mapping (a
    /// final sigma stays sigma, "İ" stays as it is), never the whole-string rules of one platform.
    /// </summary>
    public static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            folded.Append(rune.Value == 0x130 ? rune : Rune.ToLowerInvariant(rune));
        }

        return folded.ToString();
    }

    /// <summary>Whether <paramref name="query"/> narrows anything: false for blank text and for lone "@" and "#" words.</summary>
    public static bool HasCriteria(string query)
    {
        return IsId(query) || !Read(query).IsEmpty;
    }

    /// <summary>The task or step an ID query ("142", "142b") names, by number only (no word fallback): what "go to task" uses. Empty when none.</summary>
    public static IReadOnlyList<TaskSearchHit> FindByNumber(IEnumerable<TaskItem> tasks, string query)
    {
        string text = Fold(query.Trim());
        return IdPattern().IsMatch(text) ? FindById(tasks.Where(task => task.IsLive).ToList(), text) : [];
    }

    /// <summary>The hits for <paramref name="query"/> in list order (a task, then its matching steps); none when it has no criteria.</summary>
    /// <param name="labels">The labels "@label" words are looked up in; without them an "@label" word finds nothing.</param>
    public static IReadOnlyList<TaskSearchHit> Find(IEnumerable<TaskItem> tasks, string query, IReadOnlyList<TaskLabel>? labels = null)
    {
        string text = Fold(query.Trim());
        List<TaskItem> live = tasks.Where(task => task.IsLive).ToList();
        if (IdPattern().IsMatch(text) && FindById(live, text) is { Count: > 0 } byNumber)
        {
            return byNumber;
        }

        Words words = Read(text);
        if (words.IsEmpty)
        {
            return [];
        }

        List<HashSet<Guid>> labelIds = LabelIds(words, labels);
        var hits = new List<TaskSearchHit>();
        foreach (TaskItem task in live)
        {
            if (!PassesTokens(task, words, labelIds))
            {
                continue;
            }

            TaskSearchMatch? match = words.Text.Count == 0 ? (words.Labels.Count > 0 ? TaskSearchMatch.Label : TaskSearchMatch.Tag) : TextMatch(task, words.Text);
            if (match is { } found)
            {
                hits.Add(new TaskSearchHit(task, null, null, found));
            }

            List<TaskStep> steps = (task.Steps ?? []).ToList();
            for (int index = 0; words.Text.Count > 0 && index < steps.Count; index++)
            {
                if (words.Text.All(word => Fold(steps[index].Title).Contains(word, StringComparison.Ordinal)))
                {
                    hits.Add(new TaskSearchHit(task, steps[index], index, TaskSearchMatch.Step));
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// The ids of the tasks <paramref name="query"/> finds: what a view filters by. A task counts when its text holds every word
    /// (title, steps, note, tags) and it passes the "@label" and "#tag" words. Null when the query narrows nothing (show all).
    /// </summary>
    public static HashSet<Guid>? MatchingTaskIds(IEnumerable<TaskItem> tasks, string query, IReadOnlyList<TaskLabel>? labels = null)
    {
        if (!HasCriteria(query))
        {
            return null;
        }

        if (IsId(query) && Find(tasks, query) is { Count: > 0 } byNumber)
        {
            return byNumber.Select(hit => hit.Task.Id).ToHashSet();
        }

        Words words = Read(query);
        List<HashSet<Guid>> labelIds = LabelIds(words, labels);
        return tasks.Where(task => task.IsLive && PassesTokens(task, words, labelIds) && (words.Text.Count == 0 || CombinedText(task, words.Text)))
            .Select(task => task.Id).ToHashSet();
    }

    private sealed record Words(List<string> Labels, List<string> Tags, List<string> Text)
    {
        public bool IsEmpty => Labels.Count == 0 && Tags.Count == 0 && Text.Count == 0;
    }

    // Splits lower-case text on spaces; "@x" and "#x" (word characters and hyphens) are tokens, a lone "@" or "#" is dropped.
    private static Words Read(string query)
    {
        var words = new Words([], [], []);
        foreach (string token in Fold(query.Trim()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "@" or "#")
            {
                continue;
            }

            if (LabelToken().Match(token) is { Success: true } label)
            {
                words.Labels.Add(label.Groups[1].Value);
            }
            else if (TagToken().Match(token) is { Success: true } tag)
            {
                words.Tags.Add(tag.Groups[1].Value);
            }
            else
            {
                words.Text.Add(token);
            }
        }

        return words;
    }

    // A label's name as an "@" word spells it: lower case, spaces as underscores.
    private static string Spelled(string name)
    {
        return Spaces().Replace(Fold(name.Trim()), "_");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // Per "@" word, the ids of the live labels whose spelled name starts with it (an empty set: the word finds nothing).
    private static List<HashSet<Guid>> LabelIds(Words words, IReadOnlyList<TaskLabel>? labels)
    {
        return words.Labels
            .Select(word => (labels ?? []).Where(label => label.IsLive && Spelled(label.Name).StartsWith(word, StringComparison.Ordinal)).Select(label => label.Id).ToHashSet())
            .ToList();
    }

    // The "@label" words (each: the task's label is one it starts) and "#tag" words (each: a tag of the task starts with it).
    private static bool PassesTokens(TaskItem task, Words words, List<HashSet<Guid>> labelIds)
    {
        return labelIds.All(ids => task.LabelId is { } label && ids.Contains(label))
            && words.Tags.All(word => (task.Tags ?? []).Any(tag => Fold(tag).StartsWith(word, StringComparison.Ordinal)));
    }

    // Every word is somewhere in the title, a step, the note or a tag ("#home").
    private static bool CombinedText(TaskItem task, List<string> words)
    {
        string all = Fold(string.Join("\n", new[] { task.Title, task.Note ?? string.Empty }
            .Concat((task.Steps ?? []).Select(step => step.Title)).Concat((task.Tags ?? []).Select(tag => "#" + tag))));
        return words.All(word => all.Contains(word, StringComparison.Ordinal));
    }

    // What the task-level hit matched: the one field that holds every word, else the words are spread over several fields.
    private static TaskSearchMatch? TextMatch(TaskItem task, List<string> words)
    {
        bool In(string? field) => field is not null && words.All(word => Fold(field).Contains(word, StringComparison.Ordinal));
        if (In(task.Title))
        {
            return TaskSearchMatch.Title;
        }

        if (In(task.Note))
        {
            return TaskSearchMatch.Note;
        }

        if (In(string.Join(" ", (task.Tags ?? []).Select(tag => "#" + tag))))
        {
            return TaskSearchMatch.Tag;
        }

        // Words held by the steps alone give only the step hits, as before.
        return CombinedText(task, words) && !(task.Steps ?? []).Any(step => In(step.Title)) ? TaskSearchMatch.Text : null;
    }

    private static IReadOnlyList<TaskSearchHit> FindById(List<TaskItem> live, string reference)
    {
        if (TaskNumbering.Find(live, reference) is not var (task, stepIndex))
        {
            return [];
        }

        if (stepIndex is not int index)
        {
            return [new TaskSearchHit(task, null, null, TaskSearchMatch.Id)];
        }

        IReadOnlyList<TaskStep> steps = task.Steps ?? [];
        return index < steps.Count ? [new TaskSearchHit(task, steps[index], index, TaskSearchMatch.Id)] : [];
    }
}
