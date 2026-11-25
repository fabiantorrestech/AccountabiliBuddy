using System.Text;
using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Search over the task list (ADR 76, spec 03 section A): a number ("142") finds the task, a number with letters ("142b") its step;
/// anything else is read as words: "@label" (the label's name starts with it, no case; an underscore stands for a space, "@deep_w"
/// finds "Deep work"; the Add box's word rule; the Shelf's and Archive's filtered search, <see cref="Matcher"/>, also takes the short name under
/// its category, "@ai" finds "Work - AI"), "#tag" (a tag starts with it) and plain words (each found, in any order, in the
/// title, a step, the note or a tag). Every word and token must match (they AND); an "@label" no label starts with finds nothing;
/// a lone "@" or "#" is ignored. A query that looks like a number ("142", "142b"; ASCII digits only) is looked up by number first (a
/// task's former temporary numbers too); when no task or step has that number ("10am", "3pm", "1st", "2fa", "4k") it is a word.
/// Label and tag words are letters, marks, digits, connector punctuation and hyphens in any script. Case folding is one simple
/// lower-case mapping per character (<see cref="Fold"/>), the same on the phone (TaskSearch.kt). Pure; the text is the user's, so nothing here logs.
/// ADR 89: a label or tag this device doesn't show (device-only elsewhere) is found by nothing: "@label" skips a label the label test refuses, and
/// "#tag" words and plain words skip a tag the tag test refuses (<see cref="TaskScopes.LabelTest"/>, <see cref="TaskScopes.TagTest"/>); without a
/// test everything is shown.
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
    /// <param name="labelShown">ADR 89: whether this device shows a label; a label it doesn't is not looked up. Null: every label.</param>
    /// <param name="tagShown">ADR 89: whether this device shows a tag; a tag it doesn't is not searched. Null: every tag.</param>
    public static IReadOnlyList<TaskSearchHit> Find(
        IEnumerable<TaskItem> tasks, string query, IReadOnlyList<TaskLabel>? labels = null, Func<TaskLabel, bool>? labelShown = null, Func<string, bool>? tagShown = null)
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

        List<HashSet<Guid>> labelIds = LabelIds(words, labels, labelShown);
        var hits = new List<TaskSearchHit>();
        foreach (TaskItem task in live)
        {
            if (!PassesTokens(task, words, labelIds, tagShown))
            {
                continue;
            }

            TaskSearchMatch? match = words.Text.Count == 0 ? (words.Labels.Count > 0 ? TaskSearchMatch.Label : TaskSearchMatch.Tag) : TextMatch(task, words.Text, tagShown);
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

    /// <summary>Whether a query and filters narrow anything: false for a blank (or lone "@" / "#") query with every filter at its default.</summary>
    public static bool Narrows(string? query, TaskSearchFilters filters)
    {
        return !filters.IsDefault || (query is not null && HasCriteria(query));
    }

    /// <summary>
    /// The ids of the tasks <paramref name="query"/> and <paramref name="filters"/> find: what the Shelf filters by (Tasks redesign item 7). A
    /// number ("142", "142b") finds its task first (the filters still apply); otherwise see <see cref="Matcher"/>. Null when nothing is
    /// narrowed (show all).
    /// </summary>
    public static HashSet<Guid>? MatchingTaskIds(IEnumerable<TaskItem> tasks, string? query, TaskSearchFilters filters, TaskSearchContext context)
    {
        if (!Narrows(query, filters))
        {
            return null;
        }

        List<TaskItem> live = tasks.Where(task => task.IsLive).ToList();
        if (query is not null && IsId(query) && Find(live, query, null, context.LabelShown, context.TagShown) is { Count: > 0 } byNumber)
        {
            Func<TaskSearchItem, bool> filtersOnly = Matcher(null, filters, context);
            return byNumber.Select(hit => hit.Task).Where(task => filtersOnly(TaskSearchItem.Of(task))).Select(task => task.Id).ToHashSet();
        }

        Func<TaskSearchItem, bool> matches = Matcher(query, filters, context);
        return live.Where(task => matches(TaskSearchItem.Of(task))).Select(task => task.Id).ToHashSet();
    }

    /// <summary>
    /// The test one item must pass for <paramref name="query"/> and <paramref name="filters"/> (Tasks redesign 7.01-7.04, 7.21-7.31), worked out
    /// once per search: every "@label" word (a label whose full or short name starts with it), every "#tag" word (a tag that starts with it)
    /// and every plain word (somewhere in the fields ticked under Look in: names, steps, notes, "#tags"); then Bins (where the item has a bin),
    /// the label picks (✓: one of them, "No label" ✓ = no label; ✕: none of them), the tag picks (✓: every one, as typing "#tag" does; ✕:
    /// none) and When (the item's time on a day of the range). A blank query passes the words.
    /// </summary>
    public static Func<TaskSearchItem, bool> Matcher(string? query, TaskSearchFilters filters, TaskSearchContext context)
    {
        Words words = Read(query ?? string.Empty);
        List<HashSet<Guid>> labelIds = LabelIds(words, context.Labels, context.LabelShown, context.Groups);
        HashSet<Guid> shownLabels = context.Labels
            .Where(label => label.IsLive && (context.LabelShown is null || context.LabelShown(label)))
            .Select(label => label.Id)
            .ToHashSet();
        HashSet<Guid> includeLabels = filters.Labels.Where(pick => pick.Value == TaskSearchPick.Include).Select(pick => pick.Key).ToHashSet();
        HashSet<Guid> leaveOutLabels = filters.Labels.Where(pick => pick.Value == TaskSearchPick.LeaveOut).Select(pick => pick.Key).ToHashSet();
        bool includesSome = includeLabels.Count > 0 || filters.NoLabel == TaskSearchPick.Include;
        List<string> includeTags = filters.Tags.Where(pick => pick.Value == TaskSearchPick.Include).Select(pick => pick.Key).ToList();
        HashSet<string> leaveOutTags = filters.Tags.Where(pick => pick.Value == TaskSearchPick.LeaveOut).Select(pick => pick.Key).ToHashSet(StringComparer.Ordinal);
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Zone).DateTime);
        (DateOnly From, DateOnly To)? range = TaskSearchDates.Range(filters, today, context.FirstDayOfWeek);

        return item =>
        {
            // ADR 89: a label this device doesn't show reads as No label; a tag it doesn't show is not there.
            Guid? label = item.LabelId is Guid id && shownLabels.Contains(id) ? id : null;
            List<string> tags = item.Tags.Where(tag => context.TagShown is null || context.TagShown(tag)).Select(Fold).ToList();

            bool wordsMatch = labelIds.All(ids => label is Guid has && ids.Contains(has))
                && words.Tags.All(word => tags.Any(tag => tag.StartsWith(word, StringComparison.Ordinal)))
                && (words.Text.Count == 0 || LookIn(item, words.Text, filters, tags));
            if (!wordsMatch)
            {
                return false;
            }

            if (item.Bin is TaskSearchBin bin && !filters.HasBin(bin))
            {
                return false;
            }

            bool labelIncluded = label is Guid included ? includeLabels.Contains(included) : filters.NoLabel == TaskSearchPick.Include;
            bool labelLeftOut = label is Guid leftOut ? leaveOutLabels.Contains(leftOut) : filters.NoLabel == TaskSearchPick.LeaveOut;
            if ((includesSome && !labelIncluded) || labelLeftOut)
            {
                return false;
            }

            if (!includeTags.All(tags.Contains) || tags.Any(leaveOutTags.Contains))
            {
                return false;
            }

            if (range is not var (from, to))
            {
                return true;
            }

            if (item.When is not DateTimeOffset when)
            {
                return false;
            }

            DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(when, context.Zone).DateTime);
            return day >= from && day <= to;
        };
    }

    /// <summary>The "#tag" words typed in <paramref name="query"/>, folded, without "#" (7.26: their chips show ✓).</summary>
    public static IReadOnlyList<string> TagWords(string? query)
    {
        return Read(query ?? string.Empty).Tags;
    }

    /// <summary><paramref name="query"/> without its "#tag" words for <paramref name="tag"/> (any case), the rest as typed (7.27).</summary>
    public static string WithoutTagWord(string query, string tag)
    {
        string key = TaskSearchFilters.TagKey(tag);
        IEnumerable<string> kept = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !(TagToken().Match(Fold(token)) is { Success: true } match && match.Groups[1].Value == key));
        return string.Join(" ", kept);
    }

    // Every plain word somewhere in the fields ticked under Look in (7.01): the name, the steps, the note, the tags ("#home").
    private static bool LookIn(TaskSearchItem item, List<string> words, TaskSearchFilters filters, List<string> foldedTags)
    {
        var fields = new List<string>();
        if (filters.InNames)
        {
            fields.Add(item.Title);
        }

        if (filters.InSteps)
        {
            fields.AddRange(item.Steps);
        }

        if (filters.InNotes && item.Note is not null)
        {
            fields.Add(item.Note);
        }

        if (filters.InTags)
        {
            fields.AddRange(foldedTags.Select(tag => "#" + tag));
        }

        string all = Fold(string.Join("\n", fields));
        return words.All(word => all.Contains(word, StringComparison.Ordinal));
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

    // Per "@" word, the ids of the live labels whose spelled name starts with it, or (with the categories known) whose short name under
    // its category does ("@ai" finds "Work - AI" under Work, 7.03). An empty set: the word finds nothing.
    private static List<HashSet<Guid>> LabelIds(
        Words words, IReadOnlyList<TaskLabel>? labels, Func<TaskLabel, bool>? labelShown, IReadOnlyList<TaskLabelGroup>? groups = null)
    {
        IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(groups);
        List<(Guid Id, string Full, string Short)> names = (labels ?? [])
            .Where(label => label.IsLive && (labelShown is null || labelShown(label)))
            .Select(label => (label.Id, Spelled(label.Name), Spelled(TaskLabelShortNames.Of(label.Name, TaskLabelGroupBook.GroupOf(label, liveGroups)?.Name))))
            .ToList();
        return words.Labels
            .Select(word => names
                .Where(name => name.Full.StartsWith(word, StringComparison.Ordinal) || name.Short.StartsWith(word, StringComparison.Ordinal))
                .Select(name => name.Id)
                .ToHashSet())
            .ToList();
    }

    // The "@label" words (each: the task's label is one it starts) and "#tag" words (each: a tag of the task starts with it).
    private static bool PassesTokens(TaskItem task, Words words, List<HashSet<Guid>> labelIds, Func<string, bool>? tagShown)
    {
        return labelIds.All(ids => task.LabelId is { } label && ids.Contains(label))
            && words.Tags.All(word => Shown(task, tagShown).Any(tag => Fold(tag).StartsWith(word, StringComparison.Ordinal)));
    }

    // The task's tags this device shows (ADR 89); all of them without a test.
    private static IEnumerable<string> Shown(TaskItem task, Func<string, bool>? tagShown)
    {
        return (task.Tags ?? []).Where(tag => tagShown is null || tagShown(tag));
    }

    // Every word is somewhere in the title, a step, the note or a tag ("#home").
    private static bool CombinedText(TaskItem task, List<string> words, Func<string, bool>? tagShown)
    {
        string all = Fold(string.Join("\n", new[] { task.Title, task.Note ?? string.Empty }
            .Concat((task.Steps ?? []).Select(step => step.Title)).Concat(Shown(task, tagShown).Select(tag => "#" + tag))));
        return words.All(word => all.Contains(word, StringComparison.Ordinal));
    }

    // What the task-level hit matched: the one field that holds every word, else the words are spread over several fields.
    private static TaskSearchMatch? TextMatch(TaskItem task, List<string> words, Func<string, bool>? tagShown)
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

        if (In(string.Join(" ", Shown(task, tagShown).Select(tag => "#" + tag))))
        {
            return TaskSearchMatch.Tag;
        }

        // Words held by the steps alone give only the step hits, as before.
        return CombinedText(task, words, tagShown) && !(task.Steps ?? []).Any(step => In(step.Title)) ? TaskSearchMatch.Text : null;
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
