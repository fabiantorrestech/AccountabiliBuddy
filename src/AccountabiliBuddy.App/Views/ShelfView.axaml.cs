using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Shelf column. Code-behind: the "Shelf ▾" and "Bins ▾" menus are built when they open, Esc in the search pill clears it,
/// <see cref="Footer"/> is what the page shows under the bins (the Archive), and the Shelf's dialogs open over the window: Create /
/// Edit view and the weekly tidy-up.
/// </summary>
public partial class ShelfView : UserControl
{
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<ShelfView, object?>(nameof(Footer));

    private ShelfViewModel? _subscribed;

    public ShelfView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.ViewEditorRequested -= OnViewEditorRequested;
            _subscribed.TidyUpRequested -= OnTidyUpRequested;
        }

        _subscribed = DataContext as ShelfViewModel;
        if (_subscribed is not null)
        {
            _subscribed.ViewEditorRequested += OnViewEditorRequested;
            _subscribed.TidyUpRequested += OnTidyUpRequested;
        }
    }

    // "Create a view…" (null) or "Edit view…" from the View menu.
    private void OnViewEditorRequested(object? sender, Core.Domain.TaskView? view)
    {
        if (_subscribed?.ViewEditor(view) is { } editor)
        {
            _ = ViewEditorDialog.ShowAsync(ViewMenuButton, editor);
        }
    }

    private void OnTidyUpRequested(object? sender, TidyUpViewModel tidy)
    {
        _ = TidyUpDialog.ShowAsync(this, tidy);
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
