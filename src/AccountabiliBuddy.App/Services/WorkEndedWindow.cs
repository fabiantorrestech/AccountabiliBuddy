namespace AccountabiliBuddy.App.Services;

/// <summary>One stretch of work ended early (ADR 65): from the moment it was confirmed to the part's planned end.</summary>
public sealed record WorkEndedWindow(DateTimeOffset Start, DateTimeOffset End);
