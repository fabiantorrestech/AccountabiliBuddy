using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the Tasks page's lists (This session, Come back to, Later, Done today): a title with a Muted line under it.
/// A click selects it for the detail card. This session's rows are numbered and can be dragged; a come-back has a check box.
/// </summary>
public sealed partial class TaskListRowViewModel : ObservableObject
{
    public TaskListRowViewModel(
        TaskRowKey key, string title, string subText, Action<TaskListRowViewModel> select, int number = 0, bool isNow = false,
        Func<Task>? tick = null)
    {
        Key = key;
        Title = title;
        SubText = subText;
        Number = number;
        IsNow = isNow;
        SelectCommand = new RelayCommand(() => select(this));
        TickCommand = tick is null ? null : new AsyncRelayCommand(tick);
    }

    public TaskRowKey Key { get; }

    public Guid Id => Key.Id;

    public TaskRowKind Kind => Key.Kind;

    public string Title { get; }

    /// <summary>The Muted line, e.g. "2 of 3 steps left · 50 min"; empty hides it.</summary>
    public string SubText { get; }

    public bool HasSubText => SubText.Length > 0;

    /// <summary>1, 2, 3 on This session's rows; 0 elsewhere.</summary>
    public int Number { get; }

    public bool IsNumbered => Number > 0;

    /// <summary>The running task: "Now" at its right.</summary>
    public bool IsNow { get; }

    /// <summary>Only This session's open tasks drag (they are the order the session runs in).</summary>
    public bool CanDrag => Kind == TaskRowKind.Session;

    public bool IsComeBack => Kind == TaskRowKind.ComeBack;

    public bool IsDone => Kind == TaskRowKind.Done;

    /// <summary>The category filter reads these.</summary>
    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    /// <summary>The come-back's check box; null for every other row.</summary>
    public ICommand? TickCommand { get; }

    public bool HasTick => TickCommand is not null;

    public ICommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Where a dragged row would land relative to this one: -1 above, 1 below, 0 none (draws a line).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLineAbove), nameof(ShowsLineBelow))]
    public partial int DropMark { get; set; }

    public bool ShowsLineAbove => DropMark < 0;

    public bool ShowsLineBelow => DropMark > 0;
}
