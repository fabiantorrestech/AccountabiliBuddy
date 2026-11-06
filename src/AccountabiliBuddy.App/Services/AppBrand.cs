using AccountabiliBuddy.Core.Domain;
using Avalonia;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The name the app shows for itself (ADR 68): AccountabiliBuddy while guarding, ProductaBuddy in companion mode. Read
/// <see cref="Name"/> in code; XAML uses the <see cref="ResourceKey"/> app resource, which follows the mode live. The About tab and
/// names that must match something outside the app (the browser extension, the Start menu shortcut, the Google project, backup and
/// sync file types, the releases page) keep <see cref="ProductName"/>. App-wide by nature, so static. Use from the UI thread only.
/// </summary>
public static class AppBrand
{
    /// <summary>The product's name, and the name while guarding.</summary>
    public const string ProductName = "AccountabiliBuddy";

    /// <summary>The name in companion mode.</summary>
    public const string CompanionName = "ProductaBuddy";

    /// <summary>The app resource holding <see cref="Name"/>, for <c>{DynamicResource Ab.BrandName}</c>.</summary>
    public const string ResourceKey = "Ab.BrandName";

    private static bool _applied;

    public static string Name { get; private set; } = ProductName;

    /// <summary>Raised when <see cref="Name"/> changes (text built in code, such as the tray menu, is redone).</summary>
    public static event EventHandler? Changed;

    /// <summary>The name for a mode.</summary>
    public static string For(GuidanceMode mode) => mode == GuidanceMode.Companion ? CompanionName : ProductName;

    public static void Set(GuidanceMode mode)
    {
        string name = For(mode);
        if (_applied && name == Name)
        {
            return;
        }

        _applied = true;
        Name = name;
        if (Application.Current is Application app)
        {
            app.Resources[ResourceKey] = name;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
