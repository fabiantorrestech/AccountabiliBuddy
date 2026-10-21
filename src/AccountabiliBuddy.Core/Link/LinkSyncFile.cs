namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// One sync file (ADR 22) on the link, either way: the sealed bytes as base64 ("data"). The link never opens it; only
/// devices in the same device group can.
/// </summary>
public sealed record LinkSyncFile(string Data);
