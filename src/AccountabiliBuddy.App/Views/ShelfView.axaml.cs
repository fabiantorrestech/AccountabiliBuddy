using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Shelf column. Code-behind: the "Shelf ▾" and "Bins ▾" menus are built when they open, Esc in the search pill clears it, and
/// <see cref="Footer"/> is what the page shows under the bins (the Archive).
/// </summary>
public partial class ShelfView : UserControl
{
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<ShelfView, object?>(nameof(Footer));

    public ShelfView()
    {
        InitializeComponent();
    }

    /// <summary>Shown under the Shelf's sections, inside its scroll.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    // "Shelf ▾": the View menu; its "History…" opens the History popover under the same button.
    private void OnViewMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && DataContext is ShelfViewModel shelf)
        {
            TaskMenus.ShowEntries(button, shelf.ViewMenu(), PlacementMode.BottomEdgeAlignedLeft, entry =>
            {
                if (entry.ViewAction == "history" && shelf.History() is { } history)
                {
                    TaskHistoryPopover.ShowAt(button, history, PlacementMode.BottomEdgeAlignedLeft);
                }
            });
            e.Handled = true;
        }
    }

    private void OnGroupMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && DataContext is ShelfViewModel shelf)
        {
            TaskMenus.ShowEntries(button, shelf.GroupMenu(), PlacementMode.BottomEdgeAlignedRight);
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ShelfViewModel { IsSearching: true } shelf)
        {
            shelf.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
