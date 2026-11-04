using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The day plan editor's changes to a template, in minutes of the day (0–1440): snapped to 15 minutes and never
/// overlapping another part. A part with a later start than end (Sleep 23:00–07:00) crosses midnight.
/// </summary>
public static class DayTemplateEditing
{
    public const int SnapMinutes = 15;

    private const int MinutesPerDay = 24 * 60;
    private const int NewPartMinutes = 60;

    /// <summary>The nearest multiple of 15 minutes, within the day.</summary>
    public static int Snap(double minutes)
    {
        int snapped = (int)Math.Round(minutes / SnapMinutes) * SnapMinutes;
        return Math.Clamp(snapped, 0, MinutesPerDay);
    }

    /// <summary>
    /// A new hour-long part from <paramref name="startMinute"/> (rounded down to 15 minutes), shortened to end where
    /// the next part starts. In a part's time it is laid over that part at its full hour (ADR 63).
    /// </summary>
    /// <returns>The template with the new part; null when, with no categories given, that time already has a part or less than 15 minutes are free.</returns>
    /// <param name="categories">The plan's categories; none: no overlaps.</param>
    public static DayTemplate? AddAt(DayTemplate template, int startMinute, Guid categoryId, IReadOnlyList<DayCategory>? categories = null)
    {
        int start = Math.Clamp(startMinute / SnapMinutes * SnapMinutes, 0, MinutesPerDay - SnapMinutes);
        List<(int Start, int End)> taken = template.Parts
            .Where(other => !other.IsFlexible)
            .SelectMany(Ranges).ToList();
        int fullEnd = Math.Min(start + NewPartMinutes, MinutesPerDay);
        bool overTaken = taken.Any(range => range.Start <= start && start < range.End);
        if (overTaken && !MayOverlap(categoryId, categoryId, categories))
        {
            return null;
        }

        int end = overTaken ? fullEnd : taken.Where(range => range.Start > start).Select(range => range.Start).Append(fullEnd).Min();
        if (end - start < SnapMinutes)
        {
            if (!MayOverlap(categoryId, categoryId, categories))
            {
                return null;
            }

            end = fullEnd;
        }

        var part = new PlanPart(Guid.NewGuid(), categoryId, TimeOf(start), TimeOf(end));
        return template with { Parts = template.Parts.Append(part).ToList() };
    }

    /// <summary>
    /// Moves or resizes a part to <paramref name="startMinute"/>–<paramref name="endMinute"/> (snapped); with no categories given, cut
    /// back where it would run into another part. Whether it crosses midnight is decided once, when the drag starts: a night part
    /// keeps its bedtime in the evening and its wake time in the morning, a day part keeps its start before its end, so
    /// a drag never turns one into the other.
    /// </summary>
    /// <param name="crossesMidnight">Whether the part crossed midnight when the drag started.</param>
    /// <param name="stickToNeighbours">An edge being dragged stops at a neighbour's edge for the first 15 minutes past it, so lining parts up doesn't overlap them by accident (ADR 63); dragging further overlaps.</param>
    /// <returns>The changed template; null when the change is refused or less than 15 minutes would be left.</returns>
    public static DayTemplate? Move(
        DayTemplate template, Guid partId, int startMinute, int endMinute, bool crossesMidnight, IReadOnlyList<DayCategory>? categories = null, bool stickToNeighbours = false)
    {
        PlanPart? part = template.Parts.FirstOrDefault(candidate => candidate.Id == partId);
        if (part is null)
        {
            return null;
        }

        int start = Math.Min(Snap(startMinute), MinutesPerDay - SnapMinutes);
        int end = Snap(endMinute);
        if (stickToNeighbours && !crossesMidnight && !part.IsFlexible)
        {
            (start, end) = StuckToNeighbours(template, part, start, end);
        }

        List<(int Start, int End)> others = template.Parts
            // A flexible break's window lies over the work around it (schema v8): neither pushes the other.
            .Where(other => other.Id != partId && !part.IsFlexible && !other.IsFlexible && !MayOverlap(part.CategoryId, other.CategoryId, categories))
            .SelectMany(Ranges)
            .OrderBy(range => range.Start)
            .ToList();

        if (!crossesMidnight)
        {
            if (start >= end)
            {
                return null;
            }

            foreach ((int otherStart, int otherEnd) in others.Where(range => range.Start < end && start < range.End))
            {
                if (otherStart <= start)
                {
                    start = otherEnd;
                }
                else
                {
                    end = Math.Min(end, otherStart);
                }
            }

            if (end - start < SnapMinutes)
            {
                return null;
            }
        }
        else
        {
            // The wake time stays in the morning: 00:00 or 24:00 would make it an evening part of its own day.
            if (end == 0 || end >= MinutesPerDay)
            {
                return null;
            }

            // The evening piece runs from the start to midnight, the morning piece from midnight to the end.
            foreach ((int _, int otherEnd) in others.Where(range => range.End > start))
            {
                start = Math.Max(start, otherEnd);
            }

            foreach ((int otherStart, int _) in others.Where(range => range.Start < end))
            {
                end = Math.Min(end, otherStart);
            }

            if (start <= end || start >= MinutesPerDay || MinutesPerDay - start + end < SnapMinutes)
            {
                return null;
            }
        }

        PlanPart moved = part with { Start = TimeOf(start), End = TimeOf(end) };
        return template with { Parts = template.Parts.Select(candidate => candidate.Id == partId ? moved : candidate).ToList() };
    }

    // The edge that moved holds at a neighbour's edge it has gone less than a snap past.
    private static (int Start, int End) StuckToNeighbours(DayTemplate template, PlanPart part, int start, int end)
    {
        bool startMoved = start != (int)part.Start.ToTimeSpan().TotalMinutes;
        bool endMoved = end != part.EndMinutes;
        foreach ((int otherStart, int otherEnd) in template.Parts.Where(other => other.Id != part.Id && !other.IsFlexible).SelectMany(Ranges))
        {
            if (endMoved && start < otherStart && otherStart < end && end <= otherStart + SnapMinutes)
            {
                end = otherStart;
            }

            if (startMoved && otherEnd < end && otherEnd > start && start >= otherEnd - SnapMinutes)
            {
                start = otherEnd;
            }
        }

        return (start, end);
    }

    /// <summary>True when the editor may lay these two categories' parts over each other: any two (ADR 63), given the plan's categories; none: no overlaps.</summary>
    public static bool MayOverlap(Guid a, Guid b, IReadOnlyList<DayCategory>? categories)
    {
        return categories is { Count: > 0 };
    }

    /// <summary>A part as minute ranges within one day; a midnight-crossing part is its start to midnight plus midnight to its end.</summary>
    public static IEnumerable<(int Start, int End)> Ranges(PlanPart part)
    {
        int start = (int)part.Start.ToTimeSpan().TotalMinutes;
        if (!part.CrossesMidnight)
        {
            yield return (start, part.EndMinutes);
            yield break;
        }

        yield return (start, MinutesPerDay);
        if (part.EndMinutes > 0)
        {
            yield return (0, part.EndMinutes);
        }
    }

    /// <summary>Minutes of the day as a time; 1440 is 00:00 (midnight at the end of the day).</summary>
    public static TimeOnly TimeOf(int minutes)
    {
        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes % MinutesPerDay));
    }
}
