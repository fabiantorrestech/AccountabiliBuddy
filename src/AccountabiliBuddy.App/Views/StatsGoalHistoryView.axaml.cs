using AccountabiliBuddy.App.ViewModels;
using Avalonia.Controls;

namespace AccountabiliBuddy.App.Views;

/// <summary>
/// One goal in Stats (round 2 R5.4). It tells the heatmap the width of its own slot, so the year crops its oldest weeks and steps down to
/// fit (<see cref="GoalHeatmapViewModel.SetAvailableWidth"/>), as on the Goals page.
/// </summary>
public partial class StatsGoalHistoryView : UserControl
{
    public StatsGoalHistoryView()
    {
        InitializeComponent();
        HeatmapHost.SizeChanged += (_, e) => ReportWidth(e.NewSize.Width);
        DataContextChanged += (_, _) => ReportWidth(HeatmapHost.Bounds.Width);
    }

    private void ReportWidth(double width)
    {
        if (DataContext is StatsGoalHistoryViewModel history && width > 0)
        {
            history.Heatmap.SetAvailableWidth(width);
        }
    }
}
