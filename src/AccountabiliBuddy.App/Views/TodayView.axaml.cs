using System.ComponentModel;
using AccountabiliBuddy.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

public partial class TodayView : UserControl
{
    private SessionViewModel? _subscribed;
    private TodayCardState? _shownState;

    // "From your lists" by tomorrow's first task or one of tomorrow's planned tasks: read the lists as the picker opens; an item chosen closes it.
    private void OnTomorrowPickerOpening(object? sender, EventArgs e)
    {
        if (sender is not Flyout flyout)
        {
            return;
        }

        ViewModels.ListPickerViewModel? owned = flyout.Target?.DataContext switch
        {
            ViewModels.ShutdownCardViewModel card => card.TomorrowPicker,
            ViewModels.ShutdownPlanRowViewModel row => row.Picker,
            _ => null,
        };
        if (owned is not { } picker)
        {
            return;
        }

        picker.Open();
        EventHandler? closeOnPick = null;
        closeOnPick = (_, _) =>
        {
            picker.Closed -= closeOnPick;
            flyout.Hide();
        };
        picker.Closed += closeOnPick;
    }

    private TasksViewModel? _tasksPage;

    public TodayView()
    {
        InitializeComponent();
        // The page header's DataContext is the task page (Today's sections live there): Ctrl+G's rows on Today are focused here.
        TodayHeader.DataContextChanged += (_, _) => FollowTasksPage(TodayHeader.DataContext as TasksViewModel);
    }

    private void FollowTasksPage(TasksViewModel? page)
    {
        if (ReferenceEquals(page, _tasksPage))
        {
            return;
        }

        if (_tasksPage is not null)
        {
            _tasksPage.FocusTodayRowRequested -= OnFocusTodayRowRequested;
        }

        _tasksPage = page;
        if (_tasksPage is not null)
        {
            _tasksPage.FocusTodayRowRequested += OnFocusTodayRowRequested;
        }
    }

    // Ctrl+G found a task of Today's sections (round 2 R3): once the tab shows and its section unfolded, its row takes the keyboard.
    private void OnFocusTodayRowRequested(object? sender, TaskRowKey key)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Button? row = this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("listRow") && button.IsEffectivelyVisible && button.DataContext is TaskListRowViewModel candidate && candidate.Key == key);
            if (row is not null)
            {
                row.Focus(Avalonia.Input.NavigationMethod.Tab);
                row.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    // The Today sidebar rests on its rail in a window narrower than Settings › Today's "Show it when the window is at least".
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        TellSidebarWidth();
    }

    private void TellSidebarWidth()
    {
        double width = TopLevel.GetTopLevel(this)?.ClientSize.Width ?? Bounds.Width;
        if (width > 0 && DataContext is SessionViewModel { Sidebar: { } sidebar })
        {
            sidebar.SetWindowWidth(width);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TellSidebarWidth();
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnSessionPropertyChanged;
        }

        _subscribed = DataContext as SessionViewModel;
        _shownState = _subscribed?.CardState;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnSessionPropertyChanged;
        }
    }

    // The main card changed state (idle, planning, running, shutdown): the page goes back to its top, so the new state isn't cut off
    // by the old scroll offset. After layout, once the new extent is known.
    // A calendar event right-clicked on the day bar: its menu, at the pointer (ADR 64).
    private void OnDayBarEvent(object? sender, AccountabiliBuddy.Core.Domain.CalendarEvent calendarEvent)
    {
        if (sender is Control { DataContext: DayBarViewModel { EventMenu: { } menu } } bar)
        {
            CalendarEventMenuPresenter.Show(bar, menu, calendarEvent, PlacementMode.Pointer);
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only a real change of state (the property is also raised when something else changed under the same state).
        if (e.PropertyName == nameof(SessionViewModel.CardState) && sender is SessionViewModel session && session.CardState != _shownState)
        {
            _shownState = session.CardState;
            Dispatcher.UIThread.Post(() => PageScroll.ScrollToHome(), DispatcherPriority.Background);
        }
    }

    // Options (tune) in the page header (round 2, brief §3.3): under the button, aligned to its right edge.
    private void OnTodayOptionsRequested(object? sender, RoutedEventArgs e)
    {
        if (TodayHeader.DataContext is not TasksViewModel tasks)
        {
            return;
        }

        TodayHeader.SetOptionsOpen(true);
        TaskMenus.ShowEntries(TodayHeader.OptionsAnchor, tasks.TodayOptionsMenu(), PlacementMode.BottomEdgeAlignedRight,
            closed: () => TodayHeader.SetOptionsOpen(false));
        e.Handled = true;
    }
}
