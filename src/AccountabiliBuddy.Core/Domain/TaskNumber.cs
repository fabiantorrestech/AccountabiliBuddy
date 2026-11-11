using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A task's number, shown as "142" (ADR 76): permanent and sequential per account once final. Only the numbering device hands
/// out final numbers; another device gives a temporary one, shown the same way, until the final one arrives.
/// </summary>
/// <param name="Value">The number shown.</param>
/// <param name="Temporary">Given by a device that isn't the numbering device; replaced by a final number on sync.</param>
public sealed record TaskNumber(
    int Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Temporary = false);
