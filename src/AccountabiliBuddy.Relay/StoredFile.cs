namespace AccountabiliBuddy.Relay;

/// <summary>One stored sealed file as the list shows it: the etag is the SHA-256 of the bytes, base64url.</summary>
internal sealed record StoredFile(string Name, string ETag, long Size, DateTimeOffset At);
