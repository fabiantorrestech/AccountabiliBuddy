using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// When and where one part of a task was last changed (ADR 76), for the field-level merge (<see cref="TaskFieldMerge"/>).
/// </summary>
/// <param name="At">When it was changed.</param>
/// <param name="By">The device that changed it; the empty id for a value the migration made.</param>
/// <param name="Bases">
/// Title and note only: hashes (<see cref="TaskFieldValues.Hash"/>) of the values this one was edited from, oldest first, at most
/// <see cref="TaskFieldMerge.MaxBases"/>. A version whose bases hold the other version's value was made after seeing it, so it
/// simply wins; when neither holds the other's, both devices changed it on their own and the task gets a
/// <see cref="Domain.TaskConflict"/>.
/// </param>
public sealed record TaskFieldStamp(
    DateTimeOffset At,
    Guid By,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Bases = null);
