namespace AccountabiliBuddy.Relay;

internal sealed record PutResult(PutStatus Status, string ETag, DateTimeOffset At);
