using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Decides whether a change of an event's classification is a looser change (ADR 58): one that leaves the event with a less
/// strict category than it has now (Watch 3, Comms 2, Rest 1, none 0), Don't classify and removing a choice included when they
/// leave less than there was. A looser change waits for the looser-change delay; any other applies at once.
/// </summary>
public static class ClassificationGate
{
    public static bool Loosens(
        CalendarEvent calendarEvent,
        DayPlan plan,
        IReadOnlyList<EventClassification> current,
        IReadOnlyList<EventClassification> next,
        CalendarRuleSet rules)
    {
        return StrictnessOf(EventCategoryResolver.CategoryOf(calendarEvent, plan, next, rules))
               < StrictnessOf(EventCategoryResolver.CategoryOf(calendarEvent, plan, current, rules));
    }

    private static int StrictnessOf(DayCategory? category)
    {
        return category is null ? 0 : CategoryStrictness.Of(category.Behavior);
    }
}
