using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One app or site a screen-time source counts. <paramref name="Text"/> is private text of the name class: sealed at rest with
/// the device's key, never logged; a name: in a backup it travels readable like a task title.
/// </summary>
/// <remarks>
/// <see cref="Devices"/> (ST4, ADR 79 amended): the devices this entry counts on. Ids only, inside the sealed goal. Null = every device the rule
/// counts on. A rule keeps at most one entry per kind and text (<see cref="GoalAppMatching.OneEntryPerKindAndText"/>).
/// </remarks>
public sealed record GoalAppMatch(GoalAppMatchKind Kind, string Text)
{
    /// <summary>The device ids this entry counts on; null = every device the rule counts on, an empty list = none. Written only when set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? Devices { get; init; }

    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }
}
