using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One per-app rule in Settings: the process, how far the overlay moves up for it, and whether it hides during that app's calls.</summary>
public sealed partial class OverlayRuleRowViewModel : ObservableObject
{
    public OverlayRuleRowViewModel(OverlayAppRule rule)
    {
        ProcessName = rule.Process;
        OffsetY = rule.OffsetY;
        HideDuringCalls = rule.HideDuringCalls;
        CallClasses = rule.CallClasses;
        TimeOnly = rule.TimeOnly;
    }

    private IReadOnlyList<string>? CallClasses { get; }

    private bool TimeOnly { get; }

    [ObservableProperty]
    public partial string ProcessName { get; set; }

    /// <summary>Vertical shift in pixels (negative = up).</summary>
    [ObservableProperty]
    public partial decimal OffsetY { get; set; }

    [ObservableProperty]
    public partial bool HideDuringCalls { get; set; }

    public OverlayAppRule ToRule() => new(ProcessName.Trim(), 0, (int)OffsetY, HideDuringCalls, CallClasses, TimeOnly);
}
