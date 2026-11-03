using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>The per-calendar rules and the global keyword list they share (ADR 58). A calendar with no rule is Off.</summary>
public sealed record CalendarRuleSet(IReadOnlyList<CalendarRule> Rules, IReadOnlyList<KeywordCategory> GlobalKeywords)
{
    public static CalendarRuleSet Empty { get; } = new(Array.Empty<CalendarRule>(), Array.Empty<KeywordCategory>());

    /// <summary>True when no calendar has a rule that can give an event a category.</summary>
    [JsonIgnore]
    public bool IsEmpty => Rules.All(rule => rule.Mode == CalendarRuleMode.Off);

    public CalendarRule? RuleFor(string calendarId)
    {
        return Rules.FirstOrDefault(rule => rule.CalendarId == calendarId);
    }

    /// <summary>The keywords a calendar's keyword rule uses: its own list, else the global one.</summary>
    public IReadOnlyList<KeywordCategory> KeywordsFor(CalendarRule rule)
    {
        return rule.OwnKeywords ?? GlobalKeywords;
    }
}
