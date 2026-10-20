namespace AccountabiliBuddy.Core.Domain;

/// <summary>A part of the day plan placed on real dates: its category and its local window.</summary>
public sealed record PlannedPart(PlanPart Part, DayCategory Category, LocalWindow Window)
{
    public string Name => Part.Label ?? Category.Name;
}
