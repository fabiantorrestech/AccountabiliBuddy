namespace AccountabiliBuddy.Core.Link;

/// <summary>End the running session early, after the wait in <see cref="LinkEndEarlyTerms"/>, with this reason.</summary>
public sealed record LinkEndEarlyRequest(string Reason);
