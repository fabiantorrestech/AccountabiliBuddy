using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Finds where a row of the Plan timeline came from when the overlay made it (ADR 58): the calendar event behind an
/// event-made part, or the stored part a piece of a cut part belongs to. The effective plan only exists in memory and its
/// parts carry no pointer back, so both are found by category and hours.
/// </summary>
public static class PlanPartOrigin
{
    /// <summary>
    /// The classified event that made a part of <paramref name="categoryId"/> over <paramref name="window"/>: among the events of
    /// that category that overlap it, the one overlapping most (then the earliest, then the lowest id). Null when none does.
    /// </summary>
    public static CalendarEvent? EventBehind(
        IEnumerable<(CalendarEvent Event, DayCategory Category)> classified, Guid categoryId, LocalWindow window)
    {
        return classified
            .Where(pair => pair.Category.Id == categoryId)
            .Select(pair => (pair.Event, Overlap: Overlap(pair.Event, window)))
            .Where(pair => pair.Overlap > TimeSpan.Zero)
            .OrderByDescending(pair => pair.Overlap)
            .ThenBy(pair => pair.Event.LocalStart)
            .ThenBy(pair => pair.Event.Id, StringComparer.Ordinal)
            .Select(pair => pair.Event)
            .FirstOrDefault();
    }

    /// <summary>
    /// The id of the stored part of <paramref name="date"/>'s template that has <paramref name="categoryId"/> and
    /// <paramref name="label"/> and whose hours hold <paramref name="piece"/>: the part a cut piece came from. Null when none
    /// does (the part belongs to another day's template, or isn't a part the editor lists).
    /// </summary>
    public static Guid? StoredPartFor(DayPlan stored, DateOnly date, Guid categoryId, string? label, LocalWindow piece)
    {
        DateTime midnight = date.ToDateTime(TimeOnly.MinValue);
        foreach (PlanPart part in stored.TemplateFor(date).Parts.Where(part => part.CategoryId == categoryId && part.Label == label))
        {
            // A part that crosses midnight starts the evening before the date it belongs to.
            DateTime start = (part.CrossesMidnight ? midnight.AddDays(-1) : midnight) + part.Start.ToTimeSpan();
            DateTime end = midnight + TimeSpan.FromMinutes(part.EndMinutes);
            if (start <= piece.Start && end >= piece.End)
            {
                return part.Id;
            }
        }

        return null;
    }

    private static TimeSpan Overlap(CalendarEvent calendarEvent, LocalWindow window)
    {
        DateTime start = calendarEvent.LocalStart > window.Start ? calendarEvent.LocalStart : window.Start;
        DateTime end = calendarEvent.LocalEnd < window.End ? calendarEvent.LocalEnd : window.End;
        return end - start;
    }
}
