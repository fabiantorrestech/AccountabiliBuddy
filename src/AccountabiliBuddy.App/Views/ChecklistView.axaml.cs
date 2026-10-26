using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

public partial class ChecklistView : UserControl
{
    private ChecklistViewModel? _subscribed;

    public ChecklistView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.FocusEntryRequested -= OnFocusEntryRequested;
        }

        _subscribed = DataContext as ChecklistViewModel;
        if (_subscribed is not null)
        {
            _subscribed.FocusEntryRequested += OnFocusEntryRequested;
        }
    }

    // The start nudge: the keyboard goes to "Add a task" once the window shows.
    private void OnFocusEntryRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => NewTaskBox.Focus(), DispatcherPriority.Background);
    }

    // Rename: the box takes the keyboard as it opens, with the old name selected.
    private void OnRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && sender is TextBox { IsVisible: true } box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    // A category's name: clicking away keeps it, like Enter.
    private void OnCategoryNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TaskCategoryRowViewModel row })
        {
            row.SaveNameCommand.Execute(null);
        }
    }

    // Clicking away keeps the new name, like Enter; Escape has already closed the box, so nothing is saved then.
    private void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ChecklistRowViewModel { IsEditing: true } row })
        {
            row.SaveEditCommand.Execute(null);
        }
    }
}
