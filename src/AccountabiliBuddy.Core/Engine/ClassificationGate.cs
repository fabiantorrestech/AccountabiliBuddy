using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Decides whether a change of a classification is a looser change (ADR 58): one that leaves any event it covers with a less
/// strict category than it has now (Watch 3, Comms 2, Rest 1, none 0), Don't classify and removing a choice included when they
/// leave less than there was. A choice for a whole series covers every kept occurrence, each of which may keep its own choice
/// or not. A looser change waits for the looser-change delay; any other applies at once.
/// </summary>
public static class ClassificationGate
{
    /// <param name="covered">The events the change covers: the clicked event, or every kept occurrence of the series.</param>
    public static bool Loosens(
        IEnumerable<CalendarEvent> covered,
        DayPlan plan,
        IReadOnlyList<EventClassification> current,
        IReadOnlyList<EventClassification> next,
        CalendarRuleSet rules)
    {
        return covered.Any(calendarEvent =>
            StrictnessOf(EventCategoryResolver.CategoryOf(calendarEvent, plan, next, rules))
            < StrictnessOf(EventCategoryResolver.CategoryOf(calendarEvent, plan, current, rules)));
    }

    private static int StrictnessOf(DayCategory? category)
    {
        return category is null ? 0 : CategoryStrictness.Of(category.Behavior);
    }
}
