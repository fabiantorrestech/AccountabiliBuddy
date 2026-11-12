using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One app or site a screen-time source counts. <paramref name="Text"/> is private text of the name class: sealed at rest with
/// the device's key, never logged; a name: in a backup it travels readable like a task title.
/// </summary>
public sealed record GoalAppMatch(GoalAppMatchKind Kind, string Text)
{
    /// <summary>Fields written by a newer app, kept and written back unchanged (ADR 79 forward compatibility); null when there are none.</summary>
    [JsonExtensionData]
    public GoalUnknownFields? Extra { get; init; }
}
