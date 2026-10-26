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
    /// the next part starts.
    /// </summary>
    /// <returns>The template with the new part; null when that time already has a part or less than 15 minutes are free.</returns>
    /// <param name="categories">The plan's categories, so a Pre-Work part may sit over Sleep (ADR 31); none: no overlaps.</param>
    public static DayTemplate? AddAt(DayTemplate template, int startMinute, Guid categoryId, IReadOnlyList<DayCategory>? categories = null)
    {
        int start = Math.Clamp(startMinute / SnapMinutes * SnapMinutes, 0, MinutesPerDay - SnapMinutes);
        List<(int Start, int End)> taken = template.Parts
            .Where(other => !other.IsFlexible && !MayOverlap(categoryId, other.CategoryId, categories))
            .SelectMany(Ranges).ToList();
        if (taken.Any(range => range.Start <= start && start < range.End))
        {
            return null;
        }

        int end = taken.Where(range => range.Start > start).Select(range => range.Start)
            .Append(Math.Min(start + NewPartMinutes, MinutesPerDay))
            .Min();
        if (end - start < SnapMinutes)
        {
            return null;
        }

        var part = new PlanPart(Guid.NewGuid(), categoryId, TimeOf(start), TimeOf(end));
        return template with { Parts = template.Parts.Append(part).ToList() };
    }

    /// <summary>
    /// Moves or resizes a part to <paramref name="startMinute"/>–<paramref name="endMinute"/> (snapped), cut back where
    /// it would run into another part. Whether it crosses midnight is decided once, when the drag starts: a night part
    /// keeps its bedtime in the evening and its wake time in the morning, a day part keeps its start before its end, so
    /// a drag never turns one into the other.
    /// </summary>
    /// <param name="crossesMidnight">Whether the part crossed midnight when the drag started.</param>
    /// <returns>The changed template; null when the change is refused or less than 15 minutes would be left.</returns>
    public static DayTemplate? Move(
        DayTemplate template, Guid partId, int startMinute, int endMinute, bool crossesMidnight, IReadOnlyList<DayCategory>? categories = null)
    {
        PlanPart? part = template.Parts.FirstOrDefault(candidate => candidate.Id == partId);
        if (part is null)
        {
            return null;
        }

        int start = Math.Min(Snap(startMinute), MinutesPerDay - SnapMinutes);
        int end = Snap(endMinute);
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

    /// <summary>
    /// True when parts of these two categories may overlap: a Pre-Work part over Sleep (ADR 31), so an early start is
    /// guarded even before the planned wake time.
    /// </summary>
    public static bool MayOverlap(Guid a, Guid b, IReadOnlyList<DayCategory>? categories)
    {
        CategoryBehavior? first = categories?.FirstOrDefault(category => category.Id == a)?.Behavior;
        CategoryBehavior? second = categories?.FirstOrDefault(category => category.Id == b)?.Behavior;
        return (first, second) is (CategoryBehavior.PreWork, CategoryBehavior.Sleep) or (CategoryBehavior.Sleep, CategoryBehavior.PreWork);
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
