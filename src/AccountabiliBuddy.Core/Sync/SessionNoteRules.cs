namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The rules for session notes that travel between the user's own devices (ADR 87), pure so the phone's twin (<c>SessionNoteRules.kt</c>)
/// gives the same answers: the merge (newest edit wins, a stamp more than 2 minutes ahead is clamped, the same as every shared list, ADR 48;
/// a note's tombstone is kept as long as the note itself could come back, the whole 90-day window, owner decision 2026-11-14, so a backup
/// restored late never revives a deleted note), the 90-day window, which notes a device's own session shows, and what a file's part is cleaned to.
/// </summary>
public static class SessionNoteRules
{
    /// <summary>How long a note is kept (and sent), counted from when it was written: the same window as the task history (ADR 72).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    /// <returns>
    /// The merge of this device's notes and another's, tombstones included, then cut to the window; the same whichever is
    /// <paramref name="first"/>. A note missing from one side is kept (two devices writing different notes both survive).
    /// </returns>
    public static List<SyncSessionNote> Merge(IEnumerable<SyncSessionNote> first, IEnumerable<SyncSessionNote> second, DateTimeOffset now)
    {
        return InWindow(SharedListMerge.Merge(first, second, now, keepTombstones: true), now);
    }

    /// <summary>The notes as this device writes them into its file: every live note and each tombstone, inside the window, oldest first.</summary>
    public static List<SyncSessionNote> ForFile(IEnumerable<SyncSessionNote> notes, DateTimeOffset now)
    {
        return Merge(notes, [], now);
    }

    /// <summary>Drops what is older than the window.</summary>
    public static List<SyncSessionNote> InWindow(IEnumerable<SyncSessionNote> notes, DateTimeOffset now)
    {
        DateTimeOffset from = now - Window;
        return notes.Where(note => note.At > from).Select(note => note.InUtc()).ToList();
    }

    /// <summary>
    /// The texts of this device's own notes written from <paramref name="from"/> to <paramref name="to"/> (a session of this device), oldest first.
    /// A note another device wrote never shows in this device's session review or History, whatever its time (they would otherwise leak into
    /// an overlapping session). A note with no writing device yet (stored before ADR 87 and not stamped) is this device's own.
    /// </summary>
    public static List<string> Between(IEnumerable<SyncSessionNote> notes, Guid self, DateTimeOffset from, DateTimeOffset to)
    {
        return notes
            .Where(note => note.DeletedAt is null && note.Text is not null && (note.By == self || note.By == Guid.Empty) && note.At >= from && note.At <= to)
            .OrderBy(note => note.At)
            .ThenBy(note => note.Id.ToString("D"), StringComparer.Ordinal)
            .Select(note => note.Text!)
            .ToList();
    }

    /// <summary>
    /// What a file's notes are cleaned to as it opens: an entry with no id, no writing device, no time or (when live) no text is left out
    /// (leaving one out never deletes: the merge keeps what it has); a tombstone carries no text.
    /// </summary>
    public static List<SyncSessionNote> Clean(IEnumerable<SyncSessionNote?> notes)
    {
        return notes
            .OfType<SyncSessionNote>()
            .Where(note => note.Id != Guid.Empty && note.By != Guid.Empty && note.At != default && note.EditedAt != default
                && (note.DeletedAt is not null || !string.IsNullOrWhiteSpace(note.Text)))
            .Select(note => (note.DeletedAt is null ? note : note with { Text = null, SessionStart = null, ProfileId = null }).InUtc())
            .ToList();
    }
}
