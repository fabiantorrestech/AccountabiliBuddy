using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The Goals list pane's view (round 2 R5.2). Right-click, the Menu key, Shift+F10 and the row's ⋯ ask for the goal menu
/// (<see cref="MenuRequested"/>; the page opens it at the row, which gets an accent outline while it is open).
/// </summary>
public partial class GoalListView : UserControl
{
    public GoalListView()
    {
        InitializeComponent();
    }

    /// <summary>A row's goal menu is asked for: the anchor, the goal and whether the keyboard asked (the first entry takes it).</summary>
    public event EventHandler<(Control Anchor, Guid GoalId, bool FromKeyboard)>? MenuRequested;

    /// <summary>Scrolls the list so a goal's row is in view (the This week card's "N behind").</summary>
    public void BringRowIntoView(Guid goalId)
    {
        Control? row = BlockList.GetVisualDescendants()
            .OfType<Panel>()
            .FirstOrDefault(panel => panel.Classes.Contains("goalRowHost") && panel.DataContext is GoalRowViewModel { } item && item.Id == goalId);
        row?.BringIntoView();
    }

    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control row && row.DataContext is GoalRowViewModel item)
        {
            bool fromKeyboard = !e.TryGetPosition(row, out _);
            MenuRequested?.Invoke(this, (row, item.Id, fromKeyboard));
            e.Handled = true;
        }
    }
}
