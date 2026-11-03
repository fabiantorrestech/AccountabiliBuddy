using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Which day plan category a calendar event carries (ADR 58). The priority is this event, then its series, then the
/// calendar's keywords, then its every-event rule, then none (the plan stands). A "Don't classify" choice stops the search
/// there, so it beats a rule. All-day events, zero-length events and events longer than <see cref="LongestEvent"/> carry
/// nothing, and neither does a category that is gone or is Sleep or Pre-Work, even when stored.
/// </summary>
public static class EventCategoryResolver
{
    /// <summary>A longer event is ignored (it can only make the day stricter than it would be).</summary>
    public static readonly TimeSpan LongestEvent = TimeSpan.FromDays(7);

    /// <summary>True for a timed event that has a length and isn't longer than <see cref="LongestEvent"/>.</summary>
    public static bool IsUsable(CalendarEvent calendarEvent)
    {
        TimeSpan length = calendarEvent.LocalEnd - calendarEvent.LocalStart;
        return !calendarEvent.IsAllDay && length > TimeSpan.Zero && length <= LongestEvent;
    }

    /// <summary>The category of one event, or null when it carries none.</summary>
    public static DayCategory? CategoryOf(CalendarEvent calendarEvent, DayPlan plan, IReadOnlyList<EventClassification> classifications, CalendarRuleSet rules)
    {
        return CategoryOf(calendarEvent, plan, Index(classifications), rules);
    }

    /// <summary>The events that carry a category, with it, in the order given.</summary>
    public static IReadOnlyList<(CalendarEvent Event, DayCategory Category)> Classified(
        IEnumerable<CalendarEvent> events, DayPlan plan, IReadOnlyList<EventClassification> classifications, CalendarRuleSet rules)
    {
        if (classifications.Count == 0 && rules.IsEmpty)
        {
            return [];
        }

        Dictionary<(string, ClassificationScope, string), Guid?> index = Index(classifications);
        var result = new List<(CalendarEvent, DayCategory)>();
        foreach (CalendarEvent calendarEvent in events)
        {
            if (CategoryOf(calendarEvent, plan, index, rules) is DayCategory category)
            {
                result.Add((calendarEvent, category));
            }
        }

        return result;
    }

    private static Dictionary<(string, ClassificationScope, string), Guid?> Index(IReadOnlyList<EventClassification> classifications)
    {
        var index = new Dictionary<(string, ClassificationScope, string), Guid?>();
        foreach (EventClassification classification in classifications)
        {
            index[(classification.CalendarId, classification.Scope, classification.Key)] = classification.CategoryId;
        }

        return index;
    }

    private static DayCategory? CategoryOf(CalendarEvent calendarEvent, DayPlan plan, Dictionary<(string, ClassificationScope, string), Guid?> index, CalendarRuleSet rules)
    {
        if (!IsUsable(calendarEvent))
        {
            return null;
        }

        if (index.TryGetValue((calendarEvent.CalendarId, ClassificationScope.ThisEvent, calendarEvent.ClassificationKey), out Guid? chosen)
            || (calendarEvent.SeriesId is not null && index.TryGetValue((calendarEvent.CalendarId, ClassificationScope.WholeSeries, calendarEvent.SeriesId), out chosen)))
        {
            return Applicable(plan, chosen);
        }

        return Applicable(plan, FromRule(calendarEvent, rules));
    }

    private static Guid? FromRule(CalendarEvent calendarEvent, CalendarRuleSet rules)
    {
        if (rules.RuleFor(calendarEvent.CalendarId) is not CalendarRule rule)
        {
            return null;
        }

        switch (rule.Mode)
        {
            case CalendarRuleMode.EveryEvent:
                return rule.CategoryId;
            case CalendarRuleMode.Keywords:
                foreach (KeywordCategory entry in rules.KeywordsFor(rule))
                {
                    if (entry.Keyword.Length > 0 && calendarEvent.Title.Contains(entry.Keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.CategoryId;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    // Sleep and Pre-Work never come from an event: a night or a warm-up has its own logic that a meeting doesn't fit.
    private static DayCategory? Applicable(DayPlan plan, Guid? categoryId)
    {
        return categoryId is Guid id
               && plan.Category(id) is DayCategory category
               && category.Behavior is not (CategoryBehavior.Sleep or CategoryBehavior.PreWork)
            ? category
            : null;
    }
}
