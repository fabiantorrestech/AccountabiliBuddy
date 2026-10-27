namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>One sealed file on the relay, as its listing gives it. <paramref name="ETag"/> is the unquoted SHA-256 (base64url) of its bytes.</summary>
public sealed record RelayFileInfo(string Name, string ETag, long Size, DateTimeOffset At);
