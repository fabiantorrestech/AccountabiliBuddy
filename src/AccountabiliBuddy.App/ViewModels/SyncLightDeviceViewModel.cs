using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One device in the title bar's sync light: its glyph (dim when it is behind) and its line in the popover.</summary>
/// <param name="AgoText">When its data last arrived, e.g. "1 min ago"; "this PC" for this computer.</param>
/// <param name="IsSynced">In step (this PC syncing fine, or another device up to date): a green check in the popover.</param>
/// <param name="IsFailing">This PC can't sync, or another device is behind (offline or failing): a red sign in the popover. Neither
/// flag = not known yet (nothing from it so far): no sign.</param>
public sealed record SyncLightDeviceViewModel(string Name, MaterialIconKind Icon, string AgoText, bool IsDim, bool IsSynced = false, bool IsFailing = false)
{
    /// <summary>The red sign: this PC's sync problem, or another device gone quiet.</summary>
    public MaterialIconKind FailingIcon { get; init; } = MaterialIconKind.CloudOffOutline;

    /// <summary>The sign's words for screen readers.</summary>
    public string StatusName => IsSynced ? "Synced" : IsFailing ? "Not syncing" : "Not known yet";
}
