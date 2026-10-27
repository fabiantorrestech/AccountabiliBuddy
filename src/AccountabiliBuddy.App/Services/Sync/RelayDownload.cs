namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A sealed file fetched from the relay, with its unquoted etag when the relay sent one.</summary>
public sealed record RelayDownload(byte[] Bytes, string? ETag);
