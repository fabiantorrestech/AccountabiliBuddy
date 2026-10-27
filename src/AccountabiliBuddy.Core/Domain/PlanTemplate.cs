namespace AccountabiliBuddy.Core.Domain;

/// <summary>A named day template (schema v12, ADR 47), e.g. "Workday" or "Off day"; the week map points at its <see cref="Id"/>.</summary>
public sealed record PlanTemplate(Guid Id, string Name, DayTemplate Template);
