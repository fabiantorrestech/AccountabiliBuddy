using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Tasks page. Code-behind: the two columns' widths (the Today / Shelf split, a hidden column giving its room to the other), the
/// resize handle between them, keeping a title or note typed in the detail card when the box loses the keyboard, the ⋯ menus of the
/// detail card's steps, and putting the keyboard in the detail card's title or step box when a menu asks for it.
/// </summary>
public partial class TasksView : UserControl
{
    // The narrowest a shown column gets.
    private const double ColumnMinWidth = 280;

    private TasksViewModel? _subscribed;
    private bool _isSplitting;
    private int _splitWhileDragging;

    public TasksView()
    {
        InitializeComponent();
    }

    // "From your lists": read the lists as the picker opens; an item chosen closes it.
    private void OnListPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not TasksViewModel { ListPicker: var picker })
        {
            return;
        }

        picker.Open();
        picker.Closed -= OnListPickerClosed;
        picker.Closed += OnListPickerClosed;
    }

    private void OnListPickerClosed(object? sender, EventArgs e)
    {
        ListPullButton.Flyout?.Hide();
    }

    // The page stays alive while another tab shows: leaving it ends select mode, so it is not found half-done on return.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible && DataContext is TasksViewModel page)
        {
            page.LeaveSelectMode();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnPagePropertyChanged;
            _subscribed.FocusRequested -= OnFocusRequested;
        }

        _subscribed = DataContext as TasksViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnPagePropertyChanged;
            _subscribed.FocusRequested += OnFocusRequested;
        }

        ApplyColumns();
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TasksViewModel.ShowsToday) or nameof(TasksViewModel.IsShelfShown) or nameof(TasksViewModel.TodaySplit))
        {
            ApplyColumns();
        }
    }

    // Both columns: Today takes its share of the split and the Shelf the rest; one column alone takes all the room.
    private void ApplyColumns(int? split = null)
    {
        if (_subscribed is not { } page)
        {
            return;
        }

        bool both = page.ShowsToday && page.IsShelfShown;
        int today = split ?? page.TodaySplit;
        ColumnDefinition todayColumn = Page.ColumnDefinitions[1];
        ColumnDefinition shelfColumn = Page.ColumnDefinitions[3];
        todayColumn.Width = page.ShowsToday ? new GridLength(both ? today : 1, GridUnitType.Star) : GridLength.Auto;
        todayColumn.MinWidth = page.ShowsToday ? ColumnMinWidth : 0;
        shelfColumn.Width = page.IsShelfShown ? new GridLength(both ? 100 - today : 1, GridUnitType.Star) : GridLength.Auto;
        shelfColumn.MinWidth = page.IsShelfShown ? ColumnMinWidth : 0;
    }

    // ---- The resize handle: Today's share follows the pointer (30 to 70 %), kept when let go; a double-click puts it back. ----

    private void OnSplitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed || _subscribed is null)
        {
            return;
        }

        _isSplitting = true;
        _splitWhileDragging = _subscribed.TodaySplit;
        SplitGrip.Classes.Set("active", true);
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnSplitMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSplitting)
        {
            return;
        }

        double left = TodayColumn.Bounds.Left;
        double width = ShelfColumn.Bounds.Right - left;
        if (width <= 0)
        {
            return;
        }

        double percent = (e.GetPosition(Page).X - left) / width * 100;
        _splitWhileDragging = (int)Math.Round(Math.Clamp(percent, Services.Tasks.TaskLayoutPreferences.MinSplit, Services.Tasks.TaskLayoutPreferences.MaxSplit));
        ApplyColumns(_splitWhileDragging);
        e.Handled = true;
    }

    private void OnSplitReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isSplitting && _subscribed is not null)
        {
            _ = _subscribed.SetSplitAsync(_splitWhileDragging);
        }

        EndSplit();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnSplitCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndSplit();
    }

    private void OnSplitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _ = _subscribed.SetSplitAsync(Services.Tasks.TaskLayoutPreferences.DefaultSplit);
        }

        e.Handled = true;
    }

    private void EndSplit()
    {
        if (!_isSplitting)
        {
            return;
        }

        _isSplitting = false;
        SplitGrip.Classes.Set("active", false);
        ApplyColumns();
    }

    // A row's menu said Rename or Add subtask: the detail card shows that task, and its title or step box takes the keyboard. A picker's "New category…": its name box does.
    private void OnFocusRequested(object? sender, string target)
    {
        Dispatcher.UIThread.Post(() =>
        {
            string name = target switch
            {
                "step" => "DetailStepBox",
                "newCategory" => "NewCategoryBox",
                _ => "DetailTitleBox",
            };
            TextBox? box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(candidate => candidate.Name == name);
            if (box is null)
            {
                return;
            }

            box.Focus();
            if (target == "title")
            {
                box.SelectAll();
            }
        }, DispatcherPriority.Background);
    }

    // A step's ⋯ button in the detail card: the menu is built from the step each time it opens.
    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            TaskMenus.Show(button);
            e.Handled = true;
        }
    }

    // The add row's dot: which category new tasks go in.
    private void OnNewTaskCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && DataContext is TasksViewModel page)
        {
            TaskMenus.ShowEntries(button, page.NewTaskCategoryEntries(), PlacementMode.BottomEdgeAlignedLeft);
            e.Handled = true;
        }
    }

    // Esc leaves select mode; otherwise it closes the detail card, unless a text box has the focus: Esc there is left to the edit.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.Handled || DataContext is not TasksViewModel page)
        {
            return;
        }

        if (page.IsSelectMode)
        {
            page.CancelSelectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        bool editing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if (!editing && page.IsDetailOpen)
        {
            page.CloseDetailCommand.Execute(null);
            e.Handled = true;
        }
    }

    // A step's inline box (rename, a step after it) takes the keyboard as it opens.
    private void OnInlineBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
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

    private void OnDetailTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TaskDetailViewModel detail })
        {
            detail.CommitTitleCommand.Execute(null);
        }
    }

    private void OnDetailNoteLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TaskDetailViewModel detail })
        {
            detail.CommitNoteCommand.Execute(null);
        }
    }
}
