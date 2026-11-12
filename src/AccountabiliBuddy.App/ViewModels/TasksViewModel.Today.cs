using AccountabiliBuddy.App.Services.Today;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 (plan R2.1, R2.2): what the Today page's task list takes from Settings › Today ("today.layout"): whether Finished today shows
// (Options › Cards › Show finished today).
public sealed partial class TasksViewModel
{
    private TodayLayoutService? _todayLayout;

    /// <summary>Today's list shows the folded Finished today section (true without a Today layout).</summary>
    public bool ShowsFinishedOnToday => _todayLayout?.Current.ShowFinishedToday ?? true;

    /// <summary>Follows Settings › Today (set by the app once).</summary>
    public void UseTodayLayout(TodayLayoutService layout)
    {
        _todayLayout = layout;
        layout.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(ShowsFinishedOnToday));
            QueueRefresh();
        };
        QueueRefresh();
    }
}
