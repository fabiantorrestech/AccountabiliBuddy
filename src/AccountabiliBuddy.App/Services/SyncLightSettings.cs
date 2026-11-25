namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The sync light's setting and the title-bar menu's folds (document "syncLight", this PC only, never synced; in device-setting backups).
/// </summary>
/// <param name="Mode">When the sync dot shows on the title-bar menu's button.</param>
/// <param name="DevicesOpen">The menu's device list is unfolded (Tasks redesign 9.12, 9.25); open until folded once.</param>
/// <param name="ThemesOpen">The menu's theme list is unfolded (9.21, 9.25).</param>
public sealed record SyncLightSettings(SyncLightMode Mode = SyncLightMode.Always, bool DevicesOpen = true, bool ThemesOpen = false);
