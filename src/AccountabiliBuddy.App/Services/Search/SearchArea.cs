using Avalonia;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Services.Search;

/// <summary>
/// Names the parts of a page for search: a result's path is built from the <see cref="NameProperty"/>s around the text
/// (e.g. "Your day › Sleep"), and a Settings row's content says which row it is, so opening a result can switch to it.
/// </summary>
public sealed class SearchArea : AvaloniaObject
{
    public static readonly AttachedProperty<string?> NameProperty =
        AvaloniaProperty.RegisterAttached<SearchArea, Control, string?>("Name");

    /// <summary>The Settings row index (<see cref="AccountabiliBuddy.App.ViewModels.SettingsSection"/>) of a row's content; -1 (the default) elsewhere.</summary>
    public static readonly AttachedProperty<int> SettingsGroupProperty =
        AvaloniaProperty.RegisterAttached<SearchArea, Control, int>("SettingsGroup", -1);

    /// <summary>
    /// True on a button whose flyout(s) hold words search should find although they are not on the page until opened (Today's
    /// "Other ways to start", its length picker and the timers' "+"): the words are read from the flyout's content, and a result
    /// opens the flyout.
    /// </summary>
    public static readonly AttachedProperty<bool> IndexesFlyoutProperty =
        AvaloniaProperty.RegisterAttached<SearchArea, Control, bool>("IndexesFlyout");

    public static bool GetIndexesFlyout(Control control) => control.GetValue(IndexesFlyoutProperty);

    public static void SetIndexesFlyout(Control control, bool value) => control.SetValue(IndexesFlyoutProperty, value);

    public static string? GetName(Control control) => control.GetValue(NameProperty);

    public static void SetName(Control control, string? value) => control.SetValue(NameProperty, value);

    public static int GetSettingsGroup(Control control) => control.GetValue(SettingsGroupProperty);

    public static void SetSettingsGroup(Control control, int value) => control.SetValue(SettingsGroupProperty, value);
}
