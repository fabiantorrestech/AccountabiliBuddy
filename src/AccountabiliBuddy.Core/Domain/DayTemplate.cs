using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>The parts of one kind of day (a named template, or a one-off day), in no particular order.</summary>
/// <param name="Blocks">The day's block rules (schema v10, ADR 37); null = the defaults (<see cref="BlockRules.Default"/>).</param>
/// <param name="Mode">How the day is guarded (schema v12, ADR 47); Schedule = the parts, as before.</param>
/// <param name="WorkWindow">A Schedule day's simple work window (schema v12); stands in for the work part when the template has none.</param>
/// <param name="BudgetMinutes">A Budget day's productive minutes (schema v12); its parts are then only the non-productive windows.</param>
public sealed record DayTemplate(
    IReadOnlyList<PlanPart> Parts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BlockRules? Blocks = null,
    DayMode Mode = DayMode.Schedule,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkWindow? WorkWindow = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BudgetMinutes = null)
{
    public static DayTemplate Empty { get; } = new(Array.Empty<PlanPart>());

    /// <summary>The block rules this day follows.</summary>
    [JsonIgnore]
    public BlockRules Rules => Blocks ?? BlockRules.Default;

    /// <summary>Id of the part standing in for a work window; the same everywhere, so every device sorts it alike.</summary>
    public static Guid WorkWindowPartId { get; } = Guid.Parse("5c7a2e10-0f00-4b3d-8f6e-1a9c40000f00");

    /// <summary>
    /// A Schedule template that has a work window and no work parts of its own, with the window added as one work part
    /// (the first Watch category); any other template as it is.
    /// </summary>
    public DayTemplate WithWorkWindowPart(IReadOnlyList<DayCategory> categories)
    {
        if (Mode != DayMode.Schedule || WorkWindow is not WorkWindow window
            || Parts.Any(part => categories.FirstOrDefault(category => category.Id == part.CategoryId)?.Behavior.IsWork() == true)
            || categories.FirstOrDefault(category => category.Behavior == CategoryBehavior.Watch) is not DayCategory work)
        {
            return this;
        }

        return this with { Parts = [.. Parts, new PlanPart(WorkWindowPartId, work.Id, window.Start, window.End)] };
    }
}
