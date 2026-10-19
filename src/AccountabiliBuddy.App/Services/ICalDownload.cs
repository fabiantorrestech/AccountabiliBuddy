namespace AccountabiliBuddy.App.Services;

/// <summary>The result of downloading an iCal address.</summary>
/// <param name="NotModified">The server said nothing changed since <paramref name="ETag"/>/<paramref name="LastModified"/>.</param>
/// <param name="Text">The .ics text; null when <paramref name="NotModified"/>.</param>
public sealed record ICalDownload(bool NotModified, string? Text, string? ETag, string? LastModified);
