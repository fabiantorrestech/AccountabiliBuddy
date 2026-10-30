namespace AccountabiliBuddy.App.Services;

/// <summary>Where one device's battery shows: the collapsed overlay, the Ctrl+Alt panel and inside the app.</summary>
public sealed record BatteryPlaces(bool CollapsedOverlay = true, bool CtrlAltPanel = true, bool InApp = true);
