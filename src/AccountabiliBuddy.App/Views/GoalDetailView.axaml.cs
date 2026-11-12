using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// The goal detail pane's view (round 2 R5.3). It tells the heatmap the width of its own slot (never the window's), so the heatmap
/// crops weeks and steps its range down to fit (<see cref="GoalHeatmapViewModel.SetAvailableWidth"/>).
/// </summary>
public partial class GoalDetailView : UserControl
{
    public GoalDetailView()
    {
        InitializeComponent();
        HeatmapHost.SizeChanged += (_, e) => ReportHeatmapWidth(e.NewSize.Width);
        DataContextChanged += (_, _) => ReportHeatmapWidth(HeatmapHost.Bounds.Width);
    }

    /// <summary>The ⋮ next to Edit (the goal menu opens under it).</summary>
    public Control MenuAnchor => MenuButton;

    private void ReportHeatmapWidth(double width)
    {
        if (DataContext is GoalDetailViewModel detail && width > 0)
        {
            detail.Heatmap.SetAvailableWidth(width);
        }
    }
}
