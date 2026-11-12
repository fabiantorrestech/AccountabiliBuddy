using System.Text.Json;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// An item of a goal list kept as written because it holds a value this app doesn't know (a goal kind, source type, entry source,
/// reminder day, review choice of a newer app; ADR 79 forward compatibility): never counted or shown, never dropped, written back at
/// its place (<see cref="GoalNewerJson"/>). Equal by place and JSON. May hold the user's private text: sealed at rest, never logged.
/// </summary>
/// <param name="Index">Its place in the list as read.</param>
/// <param name="Json">The item as read.</param>
public sealed record GoalNewerItem(int Index, JsonElement Json)
{
    public bool Equals(GoalNewerItem? other)
    {
        return other is not null && Index == other.Index && Json.GetRawText() == other.Json.GetRawText();
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Json.GetRawText());
    }
}
