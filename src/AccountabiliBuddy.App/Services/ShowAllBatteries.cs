namespace AccountabiliBuddy.App.Services;

/// <summary>Shows every known battery everywhere: the stand-in until part 6's visibility matrix (devices x overlay / panel / app) is on main.</summary>
public sealed class ShowAllBatteries : IBatteryVisibility
{
    public bool IsShown(string deviceId, BatterySurface surface) => true;
}
