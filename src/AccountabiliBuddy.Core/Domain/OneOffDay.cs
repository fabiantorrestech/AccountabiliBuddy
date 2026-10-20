namespace AccountabiliBuddy.Core.Domain;

/// <summary>A single date planned differently (a day off, a half day); its template replaces that day's usual one.</summary>
public sealed record OneOffDay(Guid Id, DateOnly Date, DayTemplate Template, string? Note = null);
