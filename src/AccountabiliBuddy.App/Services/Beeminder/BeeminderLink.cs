namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>A goal's live Beeminder source in direction Read (ADR 84): which goal counts it and the key its source stores (the slug).</summary>
public sealed record BeeminderLink(Guid GoalId, string Key);
