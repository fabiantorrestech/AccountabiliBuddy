namespace AccountabiliBuddy.Core.Domain;

/// <summary>An "On track (this session)" answer, by its readable columns only: when, the app and the site.</summary>
public sealed record SessionAllowLabel(DateTimeOffset At, string ProcessName, string? Domain);
