namespace AccountabiliBuddy.App.Services;

/// <summary>The app version that last ran with this data (documents key "app.version").</summary>
public sealed record AppVersionRecord(string Version, DateTimeOffset FirstRunAt);
