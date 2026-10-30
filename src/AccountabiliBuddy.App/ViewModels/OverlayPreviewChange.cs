namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the overlay settings just did to the preview: the scene to jump to (null when the settings were only reloaded) and whether the change was to the left-aligned taskbar choice.</summary>
public sealed record OverlayPreviewChange(OverlayPreviewScene? Scene, bool LeftAlignedChosen = false);