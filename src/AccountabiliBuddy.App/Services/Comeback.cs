namespace AccountabiliBuddy.App.Services;

/// <summary>A thought that must not drift (redesign F/K): listed on Today until ticked.</summary>
/// <param name="DoneAt">When it was ticked; null while open.</param>
public sealed record Comeback(Guid Id, string Text, DateTimeOffset CreatedAt, DateTimeOffset? DoneAt = null);
