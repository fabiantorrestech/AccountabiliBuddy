using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// Names the parts of a page for search: a result's path is built from the <see cref="NameProperty"/>s around the text
/// (e.g. "Your day › Sleep"), and a Settings group panel says which group it is, so opening a result can switch to it.
/// </summary>
public sealed class SearchArea : AvaloniaObject
{
    public static readonly AttachedProperty<string?> NameProperty =
        AvaloniaProperty.RegisterAttached<SearchArea, Control, string?>("Name");

    /// <summary>The Settings group index of a group panel; -1 (the default) elsewhere.</summary>
    public static readonly AttachedProperty<int> SettingsGroupProperty =
        AvaloniaProperty.RegisterAttached<SearchArea, Control, int>("SettingsGroup", -1);

    public static string? GetName(Control control) => control.GetValue(NameProperty);

    public static void SetName(Control control, string? value) => control.SetValue(NameProperty, value);

    public static int GetSettingsGroup(Control control) => control.GetValue(SettingsGroupProperty);

    public static void SetSettingsGroup(Control control, int value) => control.SetValue(SettingsGroupProperty, value);
}
