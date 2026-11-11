using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// Task numbers (ADR 76): permanent, sequential per account, the same on every device. One device hands out final numbers: the
/// PC with the lowest device id (compared as the lower-case "D" text) among the group's PCs; a group without a PC has none yet
/// (the phone's run decides it). Any other device gives a temporary number (the next free one it knows of), shown the same way;
/// the numbering device makes it final on its next merge, keeping the value when no final number has it, and otherwise giving the
/// next free one and remembering the temporary one in <see cref="TaskItem.FormerNumbers"/>, so "143b" written meanwhile still
/// finds the task. Two final numbers are never alike: should a change of numbering device ever make a pair, the later task of
/// the pair gets a new number the same way.
/// </summary>
public static class TaskNumbering
{
    public const string PcKind = "pc";

    /// <summary>The numbering device among <paramref name="devices"/> (id and kind, this device included); null when none is a PC.</summary>
    public static Guid? NumberingDevice(IEnumerable<(Guid DeviceId, string Kind)> devices)
    {
        return devices
            .Where(device => string.Equals(device.Kind, PcKind, StringComparison.OrdinalIgnoreCase) && device.DeviceId != Guid.Empty)
            .Select(device => (Guid?)device.DeviceId)
            .OrderBy(id => id!.Value.ToString("D"), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The next number free of every number and former number <paramref name="tasks"/> hold (1 for none).</summary>
    public static int NextFree(IEnumerable<TaskItem> tasks)
    {
        int highest = 0;
        foreach (TaskItem task in tasks)
        {
            highest = Math.Max(highest, task.Number?.Value ?? 0);
            highest = Math.Max(highest, (task.FormerNumbers ?? []).DefaultIfEmpty(0).Max());
        }

        return highest + 1;
    }

    /// <summary>A number for a new task: final on the numbering device, temporary anywhere else.</summary>
    public static TaskNumber NewNumber(IEnumerable<TaskItem> known, bool isNumberingDevice)
    {
        return new TaskNumber(NextFree(known), Temporary: !isNumberingDevice);
    }

    /// <summary>
    /// On the numbering device only (<paramref name="self"/> == <paramref name="numberingDevice"/>): gives every live task without a
    /// final number one, and renumbers the later task of any pair with the same final number, each change stamped
    /// (<see cref="TaskEdits"/>). Tasks go in creation order, then id, so the result doesn't depend on the list's order.
    /// </summary>
    /// <param name="alsoUsed">Tasks that hold numbers outside the list (History's snapshots), so no final number repeats theirs.</param>
    /// <returns>The list with the changes; the same items elsewhere.</returns>
    public static List<SyncTaskItem> AssignFinal(
        IReadOnlyList<SyncTaskItem> tasks, Guid self, Guid? numberingDevice, DateTimeOffset now, IEnumerable<TaskItem>? alsoUsed = null)
    {
        var result = tasks.ToList();
        if (numberingDevice != self)
        {
            return result;
        }

        List<TaskItem> others = (alsoUsed ?? []).ToList();
        var finals = new HashSet<int>(others.Where(task => task.Number is { Temporary: false }).Select(task => task.Number!.Value));
        int next = NextFree(result.Select(item => item.Task).Concat(others));

        List<int> order = Enumerable.Range(0, result.Count)
            .Where(index => result[index].Task.IsLive)
            .OrderBy(index => result[index].Task.CreatedAt.UtcTicks)
            .ThenBy(index => result[index].Id.ToString("D"), StringComparer.Ordinal)
            .ToList();

        // Finals first, in order: the first holder of a number keeps it; a repeat is renumbered below.
        var needNumber = new List<int>();
        foreach (int index in order)
        {
            TaskNumber? number = result[index].Task.Number;
            if (number is { Temporary: false } && finals.Add(number.Value))
            {
                continue;
            }

            needNumber.Add(index);
        }

        foreach (int index in needNumber)
        {
            SyncTaskItem item = result[index];
            TaskNumber? old = item.Task.Number;
            int value;
            if (old is { Temporary: true } && !finals.Contains(old.Value))
            {
                value = old.Value;
            }
            else
            {
                value = next;
                next++;
            }

            finals.Add(value);
            IReadOnlyList<int>? former = item.Task.FormerNumbers;
            if (old is not null && old.Value != value)
            {
                former = (former ?? []).Append(old.Value).Distinct().Order().ToList();
            }

            TaskItem changed = item.Task with { Number = new TaskNumber(value), FormerNumbers = former };
            SyncTaskItem stamped = TaskEdits.Edit(item, TaskField.Number, changed, now, self);
            result[index] = stamped with { Task = stamped.Task with { FormerNumbers = former } };
        }

        return result;
    }

    /// <summary>
    /// The live task a reference like "142" or "142b" points to (the step's index with it, null for the task itself): a final
    /// number first, then a current temporary one, then a former number. Null when nothing matches.
    /// </summary>
    public static (TaskItem Task, int? StepIndex)? Find(IEnumerable<TaskItem> tasks, string reference)
    {
        string text = reference.Trim().ToLowerInvariant();
        int digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits == 0 || !int.TryParse(text[..digits], out int number))
        {
            return null;
        }

        int? stepIndex = null;
        if (digits < text.Length)
        {
            stepIndex = TaskStepLetters.IndexOf(text[digits..]);
            if (stepIndex is null)
            {
                return null;
            }
        }

        List<TaskItem> live = tasks.Where(task => task.IsLive).ToList();
        TaskItem? found = live.FirstOrDefault(task => task.Number is { Temporary: false } && task.Number.Value == number)
            ?? live.FirstOrDefault(task => task.Number is { Temporary: true } && task.Number.Value == number)
            ?? live.FirstOrDefault(task => task.FormerNumbers?.Contains(number) == true);
        return found is null ? null : (found, stepIndex);
    }
}
