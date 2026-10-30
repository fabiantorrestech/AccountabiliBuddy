namespace AccountabiliBuddy.App.Services;

/// <summary>Where a battery may show: the collapsed overlay, the Ctrl+Alt panel, or the app (part 6's visibility matrix).</summary>
public enum BatterySurface
{
    CollapsedOverlay,
    Panel,
    App,
}

/// <summary>Which devices' batteries show where. Part 6 stores the matrix in a document; until its key is on main, <see cref="ShowAllBatteries"/> stands in.</summary>
public interface IBatteryVisibility
{
    bool IsShown(string deviceId, BatterySurface surface);
}
