using AccountabiliBuddy.App.Controls;
using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Goals page's view (round 2 R5.1): wires the splitter (a drag's end, a key or a double-click is remembered), the list's collapse
/// to icons (from the panes' own width), the Options menu under its button, and the goal menu at a row or at the detail's ⋮.
/// </summary>
public partial class GoalsView : UserControl
{
    private GoalsViewModel? _subscribed;

    public GoalsView()
    {
        InitializeComponent();
        Splitter.ValueCommitted += (_, width) => _ = (DataContext as GoalsViewModel)?.CommitListWidthAsync(width);
        Panes.PropertyChanged += (_, e) =>
        {
            if (e.Property == SplitPanesPanel.IsListCollapsedProperty && DataContext is GoalsViewModel goals)
            {
                bool collapsed = Panes.IsListCollapsed;
                // After this layout pass: the list's own content changes with it.
                Dispatcher.UIThread.Post(() => goals.List.IsCompact = collapsed);
            }
        };
        ListPane.MenuRequested += (_, request) => ShowGoalMenu(request.Anchor, request.GoalId, request.FromKeyboard);
        DataContextChanged += (_, _) => Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.DetailMenuRequested -= OnDetailMenuRequested;
            _subscribed.BringIntoViewRequested -= OnBringIntoViewRequested;
            _subscribed.ConfirmRequested -= OnConfirmRequested;
        }

        _subscribed = DataContext as GoalsViewModel;
        if (_subscribed is not null)
        {
            _subscribed.DetailMenuRequested += OnDetailMenuRequested;
            _subscribed.BringIntoViewRequested += OnBringIntoViewRequested;
            _subscribed.ConfirmRequested += OnConfirmRequested;
        }
    }

    private void OnDetailMenuRequested(object? sender, EventArgs e)
    {
        if (DataContext is GoalsViewModel { SelectedId: Guid id })
        {
            ShowGoalMenu(DetailPane.MenuAnchor, id, false);
        }
    }

    // Delete goal…, Track time only, a session that didn't start: asked or said in a small dialog over the page.
    private async void OnConfirmRequested(object? sender, GoalConfirmRequest request)
    {
        await GoalConfirmDialog.ShowAsync(this, request);
    }

    private void OnBringIntoViewRequested(object? sender, Guid id)
    {
        Dispatcher.UIThread.Post(() => ListPane.BringRowIntoView(id), DispatcherPriority.Background);
    }

    private void ShowGoalMenu(Control anchor, Guid goalId, bool fromKeyboard)
    {
        if (DataContext is not GoalsViewModel goals)
        {
            return;
        }

        IReadOnlyList<TaskMenuEntry> entries = goals.GoalMenu(goalId);
        if (entries.Count > 0)
        {
            TaskMenus.ShowEntries(anchor, entries, fromKeyboard ? PlacementMode.BottomEdgeAlignedLeft : PlacementMode.Pointer, fromKeyboard: fromKeyboard);
        }
    }

    // Options (tune): under the button, aligned to its right edge.
    private void OnOptionsRequested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GoalsViewModel goals)
        {
            return;
        }

        GoalsHeader.SetOptionsOpen(true);
        TaskMenus.ShowEntries(GoalsHeader.OptionsAnchor, goals.OptionsMenu(), PlacementMode.BottomEdgeAlignedRight, closed: () => GoalsHeader.SetOptionsOpen(false));
        e.Handled = true;
    }
}
