namespace AccountabiliBuddy.App.Services;

/// <summary>The app version that last ran with this data (documents key "app.version").</summary>
/// <param name="Previous">The version that ran with it before this one (null on a fresh install); the migration
/// assistant shows what's new since then.</param>
public sealed record AppVersionRecord(string Version, DateTimeOffset FirstRunAt, string? Previous = null);
