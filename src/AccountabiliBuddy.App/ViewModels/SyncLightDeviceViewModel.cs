using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device in the title bar's sync light: its glyph (dim when it is behind) and its line in the popover.</summary>
/// <param name="AgoText">When its data last arrived, e.g. "1 min ago"; "this PC" for this computer.</param>
public sealed record SyncLightDeviceViewModel(string Name, MaterialIconKind Icon, string AgoText, bool IsDim);
