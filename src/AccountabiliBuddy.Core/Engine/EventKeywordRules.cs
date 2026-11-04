using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>"Always classify like this" on a calendar event (ADR 64): the keyword a title suggests, and a rule set with that keyword added.</summary>
public static partial class EventKeywordRules
{
    /// <summary>The event's title without what changes between meetings at its end: "Team standup - 11/04" gives "Team standup".</summary>
    public static string SuggestKeyword(string title)
    {
        string trimmed = TrailingDatesAndNumbers().Replace(title.Trim(), string.Empty).Trim();
        return trimmed.Length > 0 ? trimmed : title.Trim();
    }

    /// <summary>
    /// <paramref name="set"/> with the keyword added for one calendar. A calendar with no rule, or set to Off, gets a keyword rule with its own list
    /// holding only this keyword, so the global keywords don't start applying to it. A keyword rule keeps its keywords and puts this one first, since
    /// the first match wins, as a list of its own, so other calendars aren't touched.
    /// </summary>
    /// <returns>Null on a calendar whose rule gives every event one category, which keywords don't apply to.</returns>
    public static CalendarRuleSet? WithKeyword(CalendarRuleSet set, string calendarId, string keyword, Guid categoryId)
    {
        CalendarRule? rule = set.RuleFor(calendarId);
        if (rule?.Mode == CalendarRuleMode.EveryEvent)
        {
            return null;
        }

        var added = new KeywordCategory(keyword.Trim(), categoryId);
        IEnumerable<KeywordCategory> kept = rule?.Mode == CalendarRuleMode.Keywords
            ? set.KeywordsFor(rule).Where(entry => !string.Equals(entry.Keyword, added.Keyword, StringComparison.OrdinalIgnoreCase))
            : [];
        var changed = new CalendarRule(calendarId, CalendarRuleMode.Keywords, null, [added, .. kept]);
        return set with { Rules = [.. set.Rules.Where(other => other.CalendarId != calendarId), changed] };
    }

    [GeneratedRegex(@"[\s\-–—:#(]*[\d][\d\s/.\-:)]*$")]
    private static partial Regex TrailingDatesAndNumbers();
}
