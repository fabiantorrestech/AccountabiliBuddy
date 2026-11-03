namespace AccountabiliBuddy.Core.Domain;

/// <summary>One calendar's rule for giving its events a category (ADR 58).</summary>
/// <param name="CategoryId">For <see cref="CalendarRuleMode.EveryEvent"/>: the category every event gets.</param>
/// <param name="OwnKeywords">For <see cref="CalendarRuleMode.Keywords"/>: this calendar's own list; null = the rule set's global list.</param>
public sealed record CalendarRule(string CalendarId, CalendarRuleMode Mode, Guid? CategoryId = null, IReadOnlyList<KeywordCategory>? OwnKeywords = null);
