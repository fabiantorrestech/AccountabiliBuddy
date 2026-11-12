using System.Text.Json;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// ADR 79 forward compatibility: a goal enum value this app doesn't know, written by a newer app (<see cref="GoalEnumConverter{T}"/>).
/// A goal list (<see cref="GoalNewerJson"/>) keeps the item that holds it as written instead of refusing the whole list; any other
/// <see cref="JsonException"/> still means the JSON is damaged.
/// </summary>
public sealed class GoalNewerValueException(string message) : JsonException(message);
