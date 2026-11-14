using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A session note (ADR 87) as the sealed sync files carry it in <see cref="SyncFile.SessionNotes"/>, merged item by item by
/// <see cref="SharedListMerge"/>: a line the user typed in Focus mode. Its text is the user's private text: only inside sealed
/// files between the user's own devices, never in a QR file, the watch's snapshot, a log or the copy a receiver keeps of the file.
/// A deleted note travels as a tombstone with no text (kept 30 days). Session reasons never travel (ADR 72, 87).
/// </summary>
/// <param name="Text">The note; null on a tombstone.</param>
/// <param name="At">When it was written; the 90-day window (<see cref="SessionNoteRules.Window"/>) counts from it.</param>
/// <param name="SessionStart">The start of the session it was written in, the writing device's own session; null for a note from before sessions were matched.</param>
/// <param name="ProfileId">The focus profile of that session (an id, never a window title, app or URL).</param>
/// <param name="By">The device that wrote it (an id only); a session review shows only its own device's notes.</param>
public sealed record SyncSessionNote(
    Guid Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    DateTimeOffset At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SessionStart,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ProfileId,
    Guid By,
    DateTimeOffset EditedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeletedAt = null) : ISharedItem
{
    /// <summary>Oldest first, as the notes are written.</summary>
    [JsonIgnore]
    public double Position => At.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public string TieBreak => string.Join(
        "\u0001", Text ?? string.Empty, At.UtcTicks.ToString(), SessionStart?.UtcTicks.ToString() ?? string.Empty,
        ProfileId?.ToString("D") ?? string.Empty, By.ToString("D"));

    public ISharedItem WithStamps(DateTimeOffset editedAt, DateTimeOffset? deletedAt)
    {
        return this with { EditedAt = editedAt, DeletedAt = deletedAt };
    }

    /// <summary>The note as a tombstone: its text and session are gone, and it is deleted at <paramref name="at"/>.</summary>
    public SyncSessionNote Deleted(DateTimeOffset at)
    {
        return this with { Text = null, SessionStart = null, ProfileId = null, EditedAt = at, DeletedAt = at };
    }
}
