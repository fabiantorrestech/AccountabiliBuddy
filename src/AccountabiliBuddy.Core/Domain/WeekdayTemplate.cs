namespace AccountabiliBuddy.Core.Domain;

/// <summary>One weekday of a week map (schema v12, ADR 47): which template that weekday follows.</summary>
public sealed record WeekdayTemplate(DayOfWeek Day, Guid TemplateId);
