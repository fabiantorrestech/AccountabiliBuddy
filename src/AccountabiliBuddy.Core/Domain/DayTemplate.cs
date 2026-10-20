namespace AccountabiliBuddy.Core.Domain;

/// <summary>The parts of one kind of day (workdays, other days, or a one-off day), in no particular order.</summary>
public sealed record DayTemplate(IReadOnlyList<PlanPart> Parts)
{
    public static DayTemplate Empty { get; } = new(Array.Empty<PlanPart>());
}
