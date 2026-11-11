namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// "Changed on both devices" (ADR 76): the title or the note was changed on two devices since the last value they agreed on.
/// The task shows the newer version; this keeps the other one until the user picks. Sealed text, like the field itself.
/// </summary>
/// <param name="Field"><see cref="TitleField"/> or <see cref="NoteField"/>.</param>
/// <param name="OtherValue">The version not shown (null for a removed note).</param>
/// <param name="OtherDeviceId">The device that made the other version.</param>
/// <param name="OtherEditedAt">When the other version was made.</param>
public sealed record TaskConflict(string Field, string? OtherValue, Guid OtherDeviceId, DateTimeOffset OtherEditedAt)
{
    public const string TitleField = "title";
    public const string NoteField = "note";
}
