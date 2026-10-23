namespace AccountabiliBuddy.Core.Link;

/// <summary>An open come-back on the PC (ADR 30), as paired devices get it in the state message.</summary>
public sealed record LinkComeback(Guid Id, string Text, DateTimeOffset CreatedAt);
