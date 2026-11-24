using System.Windows.Input;
using AccountabiliBuddy.App.Services.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What the Tasks page's Options menu (<see cref="PageOptionsMenus.Tasks"/>) shows and does, read when it opens. A null action leaves its
/// row out (Label dot without the task settings, Fade without the layout store, the Today rows without Today's layout).
/// </summary>
public sealed record TasksOptionsState(
    bool Compact,
    ICommand NormalRows,
    ICommand CompactRows,
    IReadOnlyList<TaskMenuEntry> SortMenu,
    string? SortText,
    TaskLabelStyle LabelDot,
    Func<TaskLabelStyle, Task>? SetLabelDot,
    bool FadeIdle,
    Func<bool, Task>? SetFadeIdle,
    IReadOnlyList<TaskMenuEntry> ViewMenu,
    string? ViewText,
    bool Expanded,
    ICommand? ToggleExpand,
    bool ShowsFinishedOnToday,
    ICommand? ToggleFinishedOnToday,
    bool ShowsMoreToday,
    ICommand? ToggleMoreToday,
    bool HasHistory);
