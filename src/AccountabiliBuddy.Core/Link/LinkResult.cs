namespace AccountabiliBuddy.Core.Link;

/// <summary>How a request went, or why a connection was refused.</summary>
public sealed record LinkResult(bool Ok, string Message);
