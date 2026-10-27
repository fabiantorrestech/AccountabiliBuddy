namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A "changed" event from the relay's push stream. An empty <paramref name="ETag"/> means the file was deleted.</summary>
public sealed record RelayChange(string Name, string ETag);
