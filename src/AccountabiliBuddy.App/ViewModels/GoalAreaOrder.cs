using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Puts the goal areas in a new order (round 2 R5.4: Edit area's order, Options › Areas' ↑ ↓): positions 1..N, saving only those that moved.</summary>
public static class GoalAreaOrder
{
    /// <summary>Moves <paramref name="areaId"/> to <paramref name="index"/> among the live areas (clamped) and saves the new positions.</summary>
    public static async Task MoveAsync(GoalsService goals, Guid areaId, int index, object? source = null)
    {
        List<GoalArea> ordered = goals.Lists.Areas.Where(area => area.IsLive).OrderBy(area => area.Position).ToList();
        GoalArea? moving = ordered.FirstOrDefault(area => area.Id == areaId);
        if (moving is null)
        {
            return;
        }

        ordered.Remove(moving);
        ordered.Insert(Math.Clamp(index, 0, ordered.Count), moving);
        for (int i = 0; i < ordered.Count; i++)
        {
            double position = i + 1;
            if (ordered[i].Position != position)
            {
                await goals.SaveAreaAsync(ordered[i] with { Position = position }, source);
            }
        }
    }
}
