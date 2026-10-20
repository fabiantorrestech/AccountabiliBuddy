namespace AccountabiliBuddy.Core.Link;

/// <summary>Start a session on the PC: a profile for <paramref name="Minutes"/>, or a count-up with the Quick Start profile.</summary>
public sealed record LinkStartRequest(Guid? ProfileId, int? Minutes, bool CountUp);
