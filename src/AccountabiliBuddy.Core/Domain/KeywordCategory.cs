namespace AccountabiliBuddy.Core.Domain;

/// <summary>A word that, found in an event's title (any case), gives the event a category (ADR 58).</summary>
public sealed record KeywordCategory(string Keyword, Guid CategoryId);
