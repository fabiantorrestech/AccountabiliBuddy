namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Which devices' batteries show where (document "batteryVisibility", this PC only, never synced; the overlay and panel of
/// the on-screen redesign read it). A device with no entry shows everywhere.
/// </summary>
/// <param name="AlwaysShowBelow20">A device under 20% shows everywhere, even where it is unticked, until it charges.</param>
/// <param name="HideWhenFull">A full device leaves the collapsed overlay.</param>
/// <param name="Devices">Per device, by its sync id (this PC: "this-pc").</param>
public sealed record BatteryVisibilitySettings(
    bool AlwaysShowBelow20 = true,
    bool HideWhenFull = false,
    Dictionary<string, BatteryPlaces>? Devices = null)
{
    /// <summary>The key of this PC in <see cref="Devices"/>.</summary>
    public const string ThisPcKey = "this-pc";

    public BatteryPlaces PlacesOf(string key) => Devices is not null && Devices.TryGetValue(key, out BatteryPlaces? places) ? places : new BatteryPlaces();
}
