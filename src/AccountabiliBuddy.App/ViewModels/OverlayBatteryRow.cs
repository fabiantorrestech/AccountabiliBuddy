using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device's battery in the overlay or its panel: "82%" with a device icon, in Drift when low.</summary>
public sealed record OverlayBatteryRow(string DeviceId, MaterialIconKind Icon, string Text, IBrush Brush);
