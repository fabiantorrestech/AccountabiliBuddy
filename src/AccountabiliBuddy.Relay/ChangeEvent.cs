namespace AccountabiliBuddy.Relay;

/// <summary>A file in a group changed: the etag is empty when it was deleted.</summary>
internal sealed record ChangeEvent(string Name, string ETag);
