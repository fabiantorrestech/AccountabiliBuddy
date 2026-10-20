namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// What ending the running session early takes, by the PC's own rules: wait until <paramref name="AllowedFrom"/>
/// (the PC's clock), then give a reason at least <paramref name="MinReasonLength"/> characters long.
/// </summary>
public sealed record LinkEndEarlyTerms(int WaitSeconds, int MinReasonLength, DateTimeOffset AllowedFrom, string? Note);
