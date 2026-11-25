using System.Security.Cryptography;
using System.Text;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// ADR 94: session notes (ADR 87, the lines typed in Focus mode) become To sort tasks. Pure, so every device (and the phone's twin, once its
/// model catches up) makes the same task from the same note, in any order, offline:
/// <list type="bullet">
/// <item>id: <see cref="TaskIdFor"/>, a version-5 style id from the note's id in a namespace of its own;</item>
/// <item>title: <see cref="TitleOf"/>, the note's first line, at most <see cref="MaxTitleLength"/> characters with an ellipsis;</item>
/// <item>note: the full text (trimmed, cut to <see cref="SessionChecklist.MaxNoteLength"/> like every task note);</item>
/// <item>origin: "Session note Tue 11/24 4:05 PM" (<see cref="TaskOrigins.SessionNote"/>) in the converting device's time zone (a merge of
/// two zones' lines keeps the same one everywhere, <see cref="TaskFieldMerge"/>);</item>
/// <item>bin To sort at position <c>at</c> in Unix milliseconds (after every task placed by hand, the notes in the order written); created at
/// the note's <c>at</c>; <see cref="TaskItem.FromNote"/> keeps the note id, its writer, its session start and focus profile id;</item>
/// <item>every field stamp, and the task's edit and touch times, at the note's own edit time (<c>editedAt</c>, else <c>at</c>), never now, by
/// the note's writer (<c>by</c>): a device converting late never overwrites a change made since (a newer stamp wins, and an edited title or
/// note holds this one's value in its bases, so no "Changed on both devices"), and never brings back a task deleted since (its tombstone is
/// newer). The number the converting device gives it is stamped the same way (<see cref="Apply"/>), so giving it never counts as an edit.</item>
/// </list>
/// A note whose task id already exists (live, in History, as a tombstone, or held by a running session) is only deleted.
/// </summary>
public static class SessionNoteTasks
{
    /// <summary>The longest title a note gives (the ellipsis included).</summary>
    public const int MaxTitleLength = 80;

    /// <summary>The fixed namespace of the note-made task ids. The phone's twin must use the same bytes.</summary>
    public static readonly Guid NameSpace = new("c4a7e2d1-5b93-4f08-8e6a-2d71f0b9a365");

    private const string Ellipsis = "…";

    /// <summary>
    /// The task id for a note: SHA-1 over <see cref="NameSpace"/>'s bytes (RFC 4122 byte order) and the UTF-8 of the note id written as
    /// <c>"D"</c> (lower case, with dashes), as a version 5 id (the way <see cref="TaskLabel.IdFor"/> makes a label's id).
    /// </summary>
    public static Guid TaskIdFor(Guid noteId)
    {
        byte[] space = NameSpace.ToByteArray(bigEndian: true);
        byte[] name = Encoding.UTF8.GetBytes(noteId.ToString("D"));
        byte[] input = new byte[space.Length + name.Length];
        space.CopyTo(input, 0);
        name.CopyTo(input, space.Length);

        byte[] hash = SHA1.HashData(input);
        byte[] id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x50);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id, bigEndian: true);
    }

    /// <summary>
    /// The title a note gives: the text trimmed, its first line (up to the first CR or LF) trimmed; longer than <see cref="MaxTitleLength"/>
    /// UTF-16 units, its first 79 units (78 when the 79th would split a surrogate pair), end-trimmed, then "…".
    /// </summary>
    public static string TitleOf(string text)
    {
        string trimmed = text.Trim();
        int lineEnd = trimmed.IndexOfAny(['\r', '\n']);
        string first = (lineEnd < 0 ? trimmed : trimmed[..lineEnd]).Trim();
        if (first.Length <= MaxTitleLength)
        {
            return first;
        }

        int cut = MaxTitleLength - 1;
        if (char.IsHighSurrogate(first[cut - 1]))
        {
            cut--;
        }

        return first[..cut].TrimEnd() + Ellipsis;
    }

    /// <summary>The To sort task a live note becomes (see the class summary); null for a tombstone or a blank note.</summary>
    /// <param name="self">
    /// The converting device's sync id (empty while it has none): a note no device stamped (stored before ADR 87, which never travels, so only the
    /// device that wrote it holds one) gets it as its writer, so the task counts as this device's once the device has an id.
    /// </param>
    public static SyncTaskItem? ToTask(SyncSessionNote note, TimeZoneInfo zone, Guid self = default)
    {
        if (note.DeletedAt is not null || string.IsNullOrWhiteSpace(note.Text) || note.Id == Guid.Empty)
        {
            return null;
        }

        DateTimeOffset at = note.At.ToUniversalTime();
        DateTimeOffset edited = (note.EditedAt == default ? note.At : note.EditedAt).ToUniversalTime();
        Guid writer = note.By == Guid.Empty ? self : note.By;
        var origin = new TaskNoteOrigin(note.Id, writer, note.SessionStart?.ToUniversalTime(), note.ProfileId);
        var task = new TaskItem(
            TaskIdFor(note.Id), TitleOf(note.Text), TaskBin.Sort, at.ToUnixTimeMilliseconds(), at, edited, edited,
            Note: SessionChecklist.CleanNote(note.Text),
            Origin: TaskOrigins.SessionNote(at, zone),
            FromNote: origin);
        return new SyncTaskItem(task, TaskFieldStamps.ForNew(task, new TaskFieldStamp(edited, writer)));
    }

    /// <summary>
    /// What converting <paramref name="notes"/> does to <paramref name="lists"/>: a task for each live note whose task id exists nowhere (not in the
    /// tasks, live or tombstone, not in History, not <paramref name="isHeld"/>), and every live note to delete once the tasks are saved.
    /// </summary>
    /// <param name="self">This device's sync id (empty while it has none), the writer of a note no device stamped (<see cref="ToTask"/>).</param>
    /// <param name="isHeld">A task id the running (or just ended) session holds outside the list; null for none.</param>
    public static SessionNoteTaskPlan Plan(IEnumerable<SyncSessionNote> notes, TaskLists lists, TimeZoneInfo zone, Guid self, Func<Guid, bool>? isHeld = null)
    {
        var known = new HashSet<Guid>(lists.Tasks.Select(item => item.Id));
        known.UnionWith(lists.Log.Select(item => item.Entry.Task.Id));
        known.UnionWith(lists.Log.Select(item => item.Id));

        var add = new List<SyncTaskItem>();
        var tombstone = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (SyncSessionNote note in notes.Where(note => note is not null).OrderBy(note => note.At).ThenBy(note => note.Id.ToString("D"), StringComparer.Ordinal))
        {
            if (!seen.Add(note.Id) || ToTask(note, zone, self) is not SyncTaskItem task)
            {
                continue;
            }

            tombstone.Add(note.Id);
            if (!known.Contains(task.Id) && isHeld?.Invoke(task.Id) != true)
            {
                add.Add(task);
                known.Add(task.Id);
            }
        }

        return add.Count == 0 && tombstone.Count == 0 ? SessionNoteTaskPlan.Empty : new SessionNoteTaskPlan(add, tombstone);
    }

    /// <summary>
    /// <paramref name="lists"/> with the plan's tasks added and normalized (<see cref="TaskListRules.Normalize"/>) as this device does; the number
    /// the normalizing gives each new task is stamped at its note's edit time like its other fields (never now), so it can't count as a later
    /// edit that would bring back a task another device deleted since. Stamped by <paramref name="self"/> where the lists need it.
    /// </summary>
    public static TaskLists Apply(TaskLists lists, SessionNoteTaskPlan plan, Guid self, Guid? numberingDevice, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (plan.Add.Count == 0)
        {
            return lists;
        }

        Dictionary<Guid, SyncTaskItem> made = plan.Add.ToDictionary(item => item.Id);
        TaskLists added = lists with { Tasks = TaskFieldMerge.Merge(lists.Tasks, plan.Add, now, self) };
        TaskLists normalized = TaskListRules.Normalize(added, self, numberingDevice, now, zone);
        List<SyncTaskItem> tasks = normalized.Tasks
            .Select(item => item.Task.IsLive && made.TryGetValue(item.Id, out SyncTaskItem? original) ? AsMade(item, original) : item)
            .ToList();
        return normalized with { Tasks = tasks };
    }

    /// <summary>
    /// The texts a session of this device jotted, as tasks (ADR 94: the review card and History list them in place of the notes): the tasks made
    /// from notes this device wrote, live or in History (finished, archived or deleted; History keeps 90 days, as the notes were kept), that were
    /// written from <paramref name="from"/> to <paramref name="to"/> (as <see cref="SessionNoteRules.Between"/> counts notes), or typed in a session
    /// of this device that started in that window (the kept session start). A task with no writer counts as this device's only while this device has
    /// no sync id (<paramref name="self"/> empty). Oldest first, each once; the title as it is now (as History's snapshot holds it). Never another
    /// device's notes (ADR 87).
    /// </summary>
    public static List<string> Between(TaskLists lists, Guid self, DateTimeOffset from, DateTimeOffset to)
    {
        IEnumerable<TaskItem> live = lists.Tasks.Select(item => item.Task).Where(task => task.IsLive);
        IEnumerable<TaskItem> inHistory = lists.Log.Where(item => item.Entry.IsLive && item.Entry.Task.IsLive).Select(item => item.Entry.Task);
        var seen = new HashSet<Guid>();
        return live.Concat(inHistory)
            .Where(task => task.FromNote is { } origin && origin.By == self
                && ((task.CreatedAt >= from && task.CreatedAt <= to) || (origin.SessionStart is DateTimeOffset start && start >= from && start <= to)))
            .Where(task => seen.Add(task.Id))
            .OrderBy(task => task.CreatedAt)
            .ThenBy(task => task.Id.ToString("D"), StringComparer.Ordinal)
            .Select(task => task.Title)
            .ToList();
    }

    // The task as normalizing left it (its number), with the made task's stamps and edit and touch times.
    private static SyncTaskItem AsMade(SyncTaskItem normalized, SyncTaskItem made)
    {
        TaskFieldStamp at = made.Stamps.Title ?? new TaskFieldStamp(made.Task.EditedAt, Guid.Empty);
        TaskFieldStamps stamps = normalized.Stamps;
        foreach (TaskField field in TaskFieldValues.All)
        {
            TaskFieldStamp? stamp = stamps.Of(field);
            if (stamp is not null && stamp.At > at.At)
            {
                stamps = stamps.With(field, at);
            }
        }

        return new SyncTaskItem(normalized.Task with { EditedAt = made.Task.EditedAt, TouchedAt = made.Task.TouchedAt }, stamps);
    }
}
