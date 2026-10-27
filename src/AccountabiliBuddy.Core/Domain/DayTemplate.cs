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
    /// A Schedule template that has a work window and no work parts of its own, with the window added as work parts
    /// (the first Watch category): one part, or several when fixed rest, sleep or other parts of the template lie inside
    /// the window (lunch, a nap), which are left out of the work and stay as they are. A flexible break stays inside the
    /// window, as it does over a timeline's work. Pieces under 15 minutes are left out. Any other template as it is.
    /// </summary>
    public DayTemplate WithWorkWindowPart(IReadOnlyList<DayCategory> categories)
    {
        if (Mode != DayMode.Schedule || WorkWindow is not WorkWindow window
            || Parts.Any(part => categories.FirstOrDefault(category => category.Id == part.CategoryId)?.Behavior.IsWork() == true)
            || categories.FirstOrDefault(category => category.Behavior == CategoryBehavior.Watch) is not DayCategory work)
        {
            return this;
        }

        return this with { Parts = [.. Parts, .. WindowPieces(window, work)] };
    }

    // The window minus the fixed parts inside it, as work parts: the first with WorkWindowPartId, the others with ids
    // numbered after it (the same ids on every device, so they all sort alike).
    private List<PlanPart> WindowPieces(WorkWindow window, DayCategory work)
    {
        const int shortest = 15;
        int start = (int)window.Start.ToTimeSpan().TotalMinutes;
        int end = window.End == TimeOnly.MinValue ? 24 * 60 : (int)window.End.ToTimeSpan().TotalMinutes;

        var holes = new List<(int Start, int End)>();
        foreach (PlanPart part in Parts.Where(part => !part.IsFlexible))
        {
            int from = (int)part.Start.ToTimeSpan().TotalMinutes;
            if (part.CrossesMidnight)
            {
                holes.Add((from, 24 * 60));
                holes.Add((0, part.EndMinutes));
            }
            else
            {
                holes.Add((from, part.EndMinutes));
            }
        }

        var pieces = new List<PlanPart>();
        void Add(int from, int to)
        {
            if (to - from >= shortest)
            {
                Guid id = pieces.Count == 0 ? WorkWindowPartId : Guid.Parse($"5c7a2e10-0f10-4b3d-8f6e-1a9c4000{pieces.Count:x4}");
                pieces.Add(new PlanPart(id, work.Id, TimeAt(from), TimeAt(to)));
            }
        }

        int cursor = start;
        foreach ((int holeStart, int holeEnd) in holes.Where(hole => hole.End > start && hole.Start < end).OrderBy(hole => hole.Start))
        {
            if (holeStart > cursor)
            {
                Add(cursor, Math.Min(holeStart, end));
            }

            cursor = Math.Max(cursor, holeEnd);
        }

        if (cursor < end)
        {
            Add(cursor, end);
        }

        return pieces;
    }

    private static TimeOnly TimeAt(int minutes)
    {
        return minutes >= 24 * 60 ? TimeOnly.MinValue : new TimeOnly(minutes / 60, minutes % 60);
    }
}
