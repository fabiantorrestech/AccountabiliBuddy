namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One app or site a screen-time source counts. <paramref name="Text"/> is private text of the name class: sealed at rest with
/// the device's key, never logged; a name: in a backup it travels readable like a task title.
/// </summary>
public sealed record GoalAppMatch(GoalAppMatchKind Kind, string Text);
