using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AccountabiliBuddy.App.Views;

/// <summary>The Stats page: the chosen view and range, the selected day and the "Day details" fold that holds History.</summary>
public partial class StatsView : UserControl
{
    public StatsView()
    {
        InitializeComponent();
    }

    // Options (tune): under the button, aligned to its right edge (the same menu rules as the other pages).
    private void OnOptionsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is StatsViewModel stats)
        {
            TaskMenus.ShowEntries(StatsOptionsButton, stats.OptionsMenu(), Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A search result lies in the Day details (their words are indexed while the fold is shut): opens the fold, and the column
    /// that holds it, so the result can be brought into view and highlighted.
    /// </summary>
    public void OpenFoldHolding(Control control)
    {
        if (DataContext is StatsViewModel viewModel && (control == DetailsView || control.GetLogicalAncestors().Contains(DetailsView)))
        {
            viewModel.ShowDetails();
        }
    }

    /// <summary>A search result for the apps and sites in History (no control to find): opens the fold they are in.</summary>
    public void OpenDetails()
    {
        (DataContext as StatsViewModel)?.ShowDetails();
    }
}
