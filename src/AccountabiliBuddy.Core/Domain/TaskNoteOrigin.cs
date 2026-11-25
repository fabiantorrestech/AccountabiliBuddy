using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// ADR 94: where a To sort task made from a session note (ADR 87) came from, ids and times only (never text): the note, the device that
/// wrote it and the session it was typed in. Kept on the task (<see cref="TaskItem.FromNote"/>, JSON <c>fromNote</c>, written only when set,
/// so an older app reads the task as before; a copy without it never takes it away in a merge), so a session's review and History still list what was jotted in
/// it (<see cref="Tasks.SessionNoteTasks.Between"/>). Set once when the task is made and never edited; a tombstone drops it.
/// </summary>
/// <param name="NoteId">The note's id; the task's id is made from it (<see cref="Tasks.SessionNoteTasks.TaskIdFor"/>).</param>
/// <param name="By">The device that wrote the note (an id only); the converting device's id for a note stored before ADR 87 and never stamped; the empty id only when the converting device had no sync id yet.</param>
/// <param name="SessionStart">The start of the writing device's own session the note was typed in (UTC); null for none (a test session, an old note).</param>
/// <param name="ProfileId">That session's focus profile id; null for none.</param>
public sealed record TaskNoteOrigin(
    Guid NoteId,
    Guid By,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SessionStart = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ProfileId = null)
{
    /// <summary>One text per value, the same on every device, so a merge that meets two different ones keeps the same one everywhere.</summary>
    [JsonIgnore]
    public string Key => string.Join(
        "|", NoteId.ToString("D"), By.ToString("D"), SessionStart?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        ProfileId?.ToString("D") ?? string.Empty);
}
