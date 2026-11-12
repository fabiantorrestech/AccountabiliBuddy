using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Per stored goal list, the items kept as written because they hold a value this version doesn't know (ADR 79 forward compatibility,
/// <see cref="GoalNewerJson"/>): never counted or shown, never dropped, written back at their places on every save.
/// </summary>
public sealed record GoalNewerLists(
    IReadOnlyList<GoalNewerItem> Areas,
    IReadOnlyList<GoalNewerItem> Goals,
    IReadOnlyList<GoalNewerItem> Entries,
    IReadOnlyList<GoalNewerItem> Reminders,
    IReadOnlyList<GoalNewerItem> Reviews)
{
    public static GoalNewerLists None { get; } = new([], [], [], [], []);
}
