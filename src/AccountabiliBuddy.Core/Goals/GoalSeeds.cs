namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// What every device's first run makes (brief §5.3): the "Work" area and the built-in Deep work goal, with ids that are the same on
/// every device, so the two merge as one item once goals sync (ADR 79). The PC's <c>GoalDefaults</c> and the phone's
/// <c>GoalDefaults</c> make exactly these values. The merge (<see cref="GoalSyncMerge"/>) treats a copy still holding them, untouched,
/// as older than any edit, whatever its stamp: a device installed later must never wipe what the user changed on another.
/// </summary>
public static class GoalSeeds
{
    /// <summary>The "Work" area's id.</summary>
    public static Guid WorkAreaId { get; } = Guid.Parse("d33b0a11-6f0c-4e2a-9d51-0000000000a1");

    public const string WorkAreaName = "Work";
    public const string WorkAreaIcon = "work";
    public const string WorkAreaColor = "#457B9D";

    public const string DeepWorkName = "Deep work";
    public const string DeepWorkIcon = "center_focus_strong";
    public const string DeepWorkColor = "#2A9D8F";

    /// <summary>What Deep work's stored record holds as its target: a daily minutes target without an amount (the targets are views).</summary>
    public static GoalTarget DeepWorkStoredTarget { get; } = new(GoalPeriod.Day, null, GoalUnit.Minutes);

    /// <summary>The "Work" area as the first run makes it.</summary>
    public static GoalArea WorkArea(DateTimeOffset at)
    {
        return new GoalArea(WorkAreaId, WorkAreaName, WorkAreaIcon, WorkAreaColor, 0, at);
    }

    /// <summary>Deep work as the first run makes it: built in, a Time goal counting all focus, in "Work", first, on Today.</summary>
    public static Goal DeepWork(DateTimeOffset at)
    {
        return new Goal(
            Goal.DeepWorkId, WorkAreaId, DeepWorkName, DeepWorkIcon, DeepWorkColor, GoalKind.Time, DeepWorkStoredTarget,
            [GoalSources.DeepWorkRule], at, 0, at, BuiltIn: true, ShowOnToday: true);
    }

    /// <summary>Whether <paramref name="area"/> is the first run's "Work", untouched (live, its name, icon, colour and place, nothing unknown).</summary>
    public static bool IsPristine(GoalArea area)
    {
        return area.Id == WorkAreaId && area.IsLive && area.Name == WorkAreaName && area.Icon == WorkAreaIcon && area.Color == WorkAreaColor
            && area.Position == 0 && area.Extra is null;
    }

    /// <summary>
    /// Whether <paramref name="field"/> of <paramref name="goal"/> still holds what the first run made: Deep work only, the part never
    /// changed on a device (its stamp's device is the empty id) and its value the seed's.
    /// </summary>
    public static bool IsPristine(Goal goal, GoalField field, GoalFieldStamp stamp)
    {
        return goal.Id == Goal.DeepWorkId && stamp.By == Guid.Empty
            && string.Equals(GoalFieldValues.Canonical(goal, field), GoalFieldValues.Canonical(DeepWork(goal.CreatedAt), field), StringComparison.Ordinal);
    }
}
