using AccountabiliBuddy.Core.Domain;
using Avalonia;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The name the app shows for itself and the mode it runs in (ADR 68). Owner 2026-11-27: the app's name is always
/// <see cref="ProductName"/> ("AccountabiliBuddy": the window title, the title bar, the tray, notifications, every sentence that names
/// the app), in both modes; only text about the mode says <see cref="CompanionName"/> (Settings › App mode, the title bar's "PB" badge
/// and its "ProductaBuddy mode" tooltip, mode explanations). Read <see cref="Name"/> in code; XAML uses the <see cref="ResourceKey"/>
/// app resource. <see cref="IsCompanion"/> follows the mode live. App-wide by nature, so static. Use from the UI thread only.
/// </summary>
public static class AppBrand
{
    /// <summary>The product's name: the app's name in both modes.</summary>
    public const string ProductName = "AccountabiliBuddy";

    /// <summary>The companion mode's name, for text about the mode only ("Switch to ProductaBuddy", "ProductaBuddy mode").</summary>
    public const string CompanionName = "ProductaBuddy";

    /// <summary>The app resource holding <see cref="Name"/>, for <c>{DynamicResource Ab.BrandName}</c>.</summary>
    public const string ResourceKey = "Ab.BrandName";

    private static bool _applied;

    /// <summary>The app's name: always <see cref="ProductName"/>.</summary>
    public static string Name => ProductName;

    /// <summary>The app runs in ProductaBuddy (companion) mode.</summary>
    public static bool IsCompanion { get; private set; }

    /// <summary>Raised when the mode changes (<see cref="IsCompanion"/>; the title bar's badge, Today's layout).</summary>
    public static event EventHandler? Changed;

    public static void Set(GuidanceMode mode)
    {
        bool companion = mode == GuidanceMode.Companion;
        if (_applied && companion == IsCompanion)
        {
            return;
        }

        _applied = true;
        IsCompanion = companion;
        if (Application.Current is Application app)
        {
            app.Resources[ResourceKey] = ProductName;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
