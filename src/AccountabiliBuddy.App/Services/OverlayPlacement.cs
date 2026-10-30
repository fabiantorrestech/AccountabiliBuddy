namespace AccountabiliBuddy.App.Services;

/// <summary>Where the session overlay lives (ADR 51; stored as a number, keep the order). The old screen corners became the two floating ones.</summary>
public enum OverlayPlacement
{
    /// <summary>Over the taskbar, right of the app icons and ending where the tray starts (the default).</summary>
    OverTaskbar,

    /// <summary>Over the taskbar and the tray too: it brings its own clock, batteries and a ⌃ button that opens the real tray.</summary>
    CoverTray,

    /// <summary>At the taskbar's left end.</summary>
    TaskbarLeft,

    /// <summary>Right beside the tray, on top of whatever is there.</summary>
    BesideTray,

    FloatBottomRight,
    FloatBottomLeft,
}
