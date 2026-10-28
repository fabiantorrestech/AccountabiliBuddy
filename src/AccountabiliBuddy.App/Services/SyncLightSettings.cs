namespace AccountabiliBuddy.App.Services;

/// <summary>The sync light's setting (document "syncLight", this PC only, never synced).</summary>
public sealed record SyncLightSettings(SyncLightMode Mode = SyncLightMode.Always);
