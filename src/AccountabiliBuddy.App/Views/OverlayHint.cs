using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// A hint for a control on the on-screen window, shown in <see cref="OverlayHintWindow"/> instead of a Windows tooltip:
/// the on-screen window is put back on top every tick, so its own tooltips always end up behind it.
/// </summary>
public sealed class OverlayHint : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<OverlayHint, Control, string?>("Text");

    public static string? GetText(Control control) => control.GetValue(TextProperty);

    public static void SetText(Control control, string? value) => control.SetValue(TextProperty, value);
}
