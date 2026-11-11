using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Search over the task list (ADR 76, spec 03 section A): a number ("142") finds the task, a number with letters ("142b") its step;
/// anything else is text, found case-insensitively in a title, a step, a note or a tag ("#home"), and gives the task and, for each
/// step that matches, the step. A query that looks like a number matches only by number (a task's former temporary numbers too).
/// Pure; the text is the user's, so nothing here logs.
/// </summary>
public static partial class TaskSearch
{
    [GeneratedRegex(@"^\d+[a-z]{0,4}$")]
    private static partial Regex IdPattern();

    /// <summary>The hits for <paramref name="query"/> in list order (a task, then its matching steps); none for a blank query.</summary>
    public static IReadOnlyList<TaskSearchHit> Find(IEnumerable<TaskItem> tasks, string query)
    {
        string text = query.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return [];
        }

        List<TaskItem> live = tasks.Where(task => task.IsLive).ToList();
        if (IdPattern().IsMatch(text))
        {
            return FindById(live, text);
        }

        string plain = text.TrimStart('#');
        var hits = new List<TaskSearchHit>();
        foreach (TaskItem task in live)
        {
            TaskSearchMatch? match =
                task.Title.Contains(text, StringComparison.OrdinalIgnoreCase) ? TaskSearchMatch.Title
                : task.Note?.Contains(text, StringComparison.OrdinalIgnoreCase) == true ? TaskSearchMatch.Note
                : plain.Length > 0 && (task.Tags ?? []).Any(tag => tag.Contains(plain, StringComparison.OrdinalIgnoreCase)) ? TaskSearchMatch.Tag
                : null;
            if (match is { } found)
            {
                hits.Add(new TaskSearchHit(task, null, null, found));
            }

            List<TaskStep> steps = (task.Steps ?? []).ToList();
            for (int index = 0; index < steps.Count; index++)
            {
                if (steps[index].Title.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(new TaskSearchHit(task, steps[index], index, TaskSearchMatch.Step));
                }
            }
        }

        return hits;
    }

    /// <summary>The ids of the tasks <paramref name="query"/> finds (a step's hit counts for its task): what a view filters by.</summary>
    public static HashSet<Guid> MatchingTaskIds(IEnumerable<TaskItem> tasks, string query)
    {
        return Find(tasks, query).Select(hit => hit.Task.Id).ToHashSet();
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
