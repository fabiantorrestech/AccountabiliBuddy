namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The scenes of the Settings preview, in tab order (the index is the tab's position).</summary>
public enum OverlayPreviewScene
{
    /// <summary>The tile on the taskbar (or floating) as the placement, size and look say.</summary>
    Taskbar,

    /// <summary>What happens while the user's taskbar hides itself.</summary>
    AutoHide,

    /// <summary>The corner clock over a full-screen app.</summary>
    FullScreen,

    /// <summary>The Ctrl+Alt panel.</summary>
    Panel,
}