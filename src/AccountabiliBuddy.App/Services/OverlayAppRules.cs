namespace AccountabiliBuddy.App.Services;

/// <summary>The built-in per-app rules, and the lookup the overlay uses.</summary>
public static class OverlayAppRules
{
    public static IReadOnlyList<OverlayAppRule> Defaults { get; } =
    [
        new OverlayAppRule("winword", OffsetY: -40),
        new OverlayAppRule("zoom", HideDuringCalls: true, CallClasses: ["ZPContentViewWndClass", "ZPFloatToolbarClass"]),
        new OverlayAppRule("ms-teams", HideDuringCalls: true),
        new OverlayAppRule("teams", HideDuringCalls: true),
    ];

    /// <summary>The rule for the app in front, or null.</summary>
    public static OverlayAppRule? Find(IReadOnlyList<OverlayAppRule> rules, string? process)
    {
        return process is null ? null : rules.FirstOrDefault(rule => string.Equals(rule.Process, process, StringComparison.OrdinalIgnoreCase));
    }
}
