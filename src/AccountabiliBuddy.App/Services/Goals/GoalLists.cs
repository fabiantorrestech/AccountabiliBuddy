using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// Everything the goals store keeps that a backup carries and a future goals sync (ADR 79, Proposed) would merge: areas, goals, entries,
/// reminders and weekly review records, tombstones included. Each list is sealed at rest in its own document (<see cref="GoalStore"/>).
/// </summary>
public sealed record GoalLists(
    IReadOnlyList<GoalArea> Areas,
    IReadOnlyList<Goal> Goals,
    IReadOnlyList<GoalEntry> Entries,
    IReadOnlyList<GoalReminder> Reminders,
    IReadOnlyList<GoalReviewRecord> Reviews)
{
    public static GoalLists Empty { get; } = new([], [], [], [], []);

    /// <summary>The built-in Deep work goal's stored record (its targets are views, <see cref="GoalsService.DeepWork"/>); null before the migration.</summary>
    [JsonIgnore]
    public Goal? DeepWorkRecord => Goals.FirstOrDefault(goal => goal.Id == Goal.DeepWorkId && goal.IsLive);

    [JsonIgnore]
    public IEnumerable<GoalArea> LiveAreas => Areas.Where(area => area.IsLive).OrderBy(area => area.Position);

    [JsonIgnore]
    public IEnumerable<Goal> LiveGoals => Goals.Where(goal => goal.IsLive).OrderBy(goal => goal.Position);
}
