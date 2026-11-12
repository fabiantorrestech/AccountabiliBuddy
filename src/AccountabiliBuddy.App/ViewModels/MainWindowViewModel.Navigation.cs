using System.ComponentModel;
using AccountabiliBuddy.App.Services.Navigation;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Back / forward on the PC (owner 2026-11-14): where the user has been (the tab; Settings' section, the open task on Tasks, the goal
/// shown on Goals), the title bar's ‹ › buttons, the mouse's back / forward buttons, Alt+← / Alt+→ and a touchpad's two-finger swipe (each plays the page slide). Going back or forward only
/// shows a page the way the rail, a Settings row, a task row or a goal row would: every lock stays (Settings during a session shows
/// locked as usual), and it is not a user action for anything else (no timer, wait or nudge reads it).
/// </summary>
public sealed partial class MainWindowViewModel
{
    private bool _navigationConnected;

    // True while a back / forward move puts a place back, so the changes it makes aren't recorded as new places.
    private bool _restoringPlace;

    /// <summary>The back / forward history (memory only, at most 50 places).</summary>
    public NavigationHistory Navigation { get; } = new();

    /// <summary>A modal dialog (or the unsaved-settings prompt) is open: back / forward do nothing. Set by the window.</summary>
    public Func<bool>? IsModalOpen { get; set; }

    /// <summary>
    /// A back / forward move is about to show its place. The window plays the page slide around it (calling the event's Apply once
    /// the old page is out and marking it handled); with no handler the place is shown at once.
    /// </summary>
    public event EventHandler<HistoryNavigatingEventArgs>? HistoryNavigating;

    /// <summary>Ends a back / forward page slide that is still running (its place is applied at once). Set by the window.</summary>
    public Action? FinishHistoryMove { get; set; }

    public bool CanGoBack => Navigation.CanGoBack;

    public bool CanGoForward => Navigation.CanGoForward;

    /// <summary>
    /// Starts following where the user goes (called once the window has its view model): Settings' section, the open task and the
    /// goal shown. Tab changes are recorded by the window once a tab is really shown (<see cref="RecordPlace"/>), so a switch
    /// refused by the unsaved-settings prompt never lands in the history.
    /// </summary>
    public void ConnectNavigation()
    {
        if (_navigationConnected)
        {
            return;
        }

        _navigationConnected = true;
        Navigation.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        };
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedRowIndex) && SelectedTab == SettingsTab)
            {
                RecordPlace();
            }
        };
        if (Tasks is not null)
        {
            Tasks.PropertyChanged += OnTasksPlaceChanged;
        }

        ConnectGoals(Goals);
        RecordPlace();
    }

    partial void OnGoalsChanged(GoalsViewModel? oldValue, GoalsViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnGoalsPlaceChanged;
        }

        if (_navigationConnected)
        {
            ConnectGoals(newValue);
        }
    }

    private void ConnectGoals(GoalsViewModel? goals)
    {
        if (goals is not null)
        {
            goals.PropertyChanged -= OnGoalsPlaceChanged;
            goals.PropertyChanged += OnGoalsPlaceChanged;
        }
    }

    private void OnTasksPlaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TasksViewModel.Detail) or nameof(TasksViewModel.IsDetailOpen) && SelectedTab == (int)MainTab.Tasks)
        {
            RecordPlace();
        }
    }

    private void OnGoalsPlaceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoalsViewModel.SelectedId) && SelectedTab == GoalsTab)
        {
            RecordPlace();
        }
    }

    /// <summary>Where the user is now, as the history keeps it.</summary>
    public NavigationPlace CurrentPlace()
    {
        var tab = (MainTab)SelectedTab;
        return tab switch
        {
            MainTab.Settings => new NavigationPlace(tab, Section: Settings.SelectedSection),
            MainTab.Tasks => new NavigationPlace(tab, Task: Tasks?.OpenTaskKey),
            MainTab.Goals => new NavigationPlace(tab, Goal: Goals?.SelectedId),
            _ => new NavigationPlace(tab),
        };
    }

    /// <summary>The page shown (or what it shows) changed by the user's own doing: remember it. Back / forward moves are not recorded.</summary>
    public void RecordPlace()
    {
        if (!_restoringPlace && _navigationConnected)
        {
            Navigation.Record(CurrentPlace());
        }
    }

    /// <summary>False for a place whose target is gone: a deleted task or goal (or a page this PC no longer has).</summary>
    public bool PlaceExists(NavigationPlace place)
    {
        return place.Tab switch
        {
            MainTab.Tasks => Tasks is not null && (place.Task is not TaskRowKey task || Tasks.HasTask(task)),
            MainTab.Goals => place.Goal is not Guid goal || (Goals?.HasGoal(goal) ?? false),
            MainTab.Activity => ShowDevTools,
            MainTab.Plan => Plan is not null,
            MainTab.Stats => Stats is not null,
            _ => true,
        };
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        Go(HistoryDirection.Back);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        Go(HistoryDirection.Forward);
    }

    /// <summary>Goes back or forward one place (skipping places that are gone). Nothing happens while a modal dialog is open.</summary>
    public void Go(HistoryDirection direction)
    {
        // A press during a slide finishes it at once, so the next move starts from where that one ends.
        FinishHistoryMove?.Invoke();
        if (IsModalOpen?.Invoke() == true || ShowsLeavePrompt)
        {
            return;
        }

        NavigationPlace? place = direction == HistoryDirection.Back ? Navigation.GoBack(PlaceExists) : Navigation.GoForward(PlaceExists);
        if (place is null)
        {
            return;
        }

        if (SelectedTab == SettingsTab && place.Tab != MainTab.Settings && Settings.HasUnsavedChanges && Settings.HasUnsavableChanges)
        {
            // Settings holds a change it can't save: stay (the history too) and ask Save / Discard / Stay, as leaving by the rail does.
            _ = direction == HistoryDirection.Back ? Navigation.GoForward(_ => true) : Navigation.GoBack(_ => true);
            MayLeaveTab(SettingsTab, (int)place.Tab);
            return;
        }

        var navigating = new HistoryNavigatingEventArgs(direction, place.Tab == (MainTab)SelectedTab, () => Show(place));
        HistoryNavigating?.Invoke(this, navigating);
        if (!navigating.Handled)
        {
            navigating.Apply();
        }
    }

    // Puts a place back the way the rail, a Settings row, a task row or a goal row would.
    private void Show(NavigationPlace place)
    {
        _restoringPlace = true;
        try
        {
            SelectedTab = (int)place.Tab;
            switch (place.Tab)
            {
                case MainTab.Settings when place.Section is SettingsSection section:
                    Settings.Select(section);
                    break;
                case MainTab.Tasks when Tasks is not null:
                    if (place.Task is TaskRowKey task)
                    {
                        Tasks.OpenTask(task);
                    }
                    else if (Tasks.IsDetailOpen)
                    {
                        Tasks.CloseDetailCommand.Execute(null);
                    }

                    break;
                case MainTab.Goals when place.Goal is Guid goal && Goals is not null:
                    Goals.Select(goal);
                    break;
            }
        }
        finally
        {
            _restoringPlace = false;
        }
    }
}
