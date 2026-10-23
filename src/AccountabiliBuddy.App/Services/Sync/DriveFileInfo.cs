namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A file in the app's Drive folder: its Drive id, name, last change and size.</summary>
public sealed record DriveFileInfo(string Id, string Name, DateTimeOffset ModifiedAt, long Size);
