namespace AccountabiliBuddy.App.Services;

/// <summary>Where a battery may show: the collapsed overlay, the Ctrl+Alt panel, or the app (part 6's visibility matrix).</summary>
public enum BatterySurface
{
    CollapsedOverlay,
    Panel,
    App,
}

/// <summary>Which devices' batteries show where (<see cref="BatteryVisibilityService"/>).</summary>
public interface IBatteryVisibility
{
    /// <summary>Whether the device's battery shows on <paramref name="surface"/> at <paramref name="percent"/>.</summary>
    bool IsShown(string deviceId, BatterySurface surface, int percent);

    /// <summary>Raised after the matrix or its options changed.</summary>
    event EventHandler? Changed;
}
