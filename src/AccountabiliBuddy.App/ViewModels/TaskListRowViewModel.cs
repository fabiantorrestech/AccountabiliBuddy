using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the Tasks page's lists (This session, Come back to, Later, Done today, Unfinished from past sessions): a check
/// box, a title with a Muted line under it and a ⋯ menu. A click selects it for the detail card. This session's rows are
/// numbered and can be dragged. The category shows only as a 3px edge at the row's left, never as a fill.
/// </summary>
public sealed partial class TaskListRowViewModel : ObservableObject, ITaskMenuSource
{
    private readonly Func<IReadOnlyList<TaskMenuEntry>>? _menu;

    public TaskListRowViewModel(
        TaskRowKey key, string title, string subText, Action<TaskListRowViewModel> select, int number = 0, bool isNow = false,
        Func<Task>? toggle = null, bool isTicked = false, Func<IReadOnlyList<TaskMenuEntry>>? menu = null)
    {
        Key = key;
        Title = title;
        SubText = subText;
        Number = number;
        IsNow = isNow;
        IsTicked = isTicked;
        _menu = menu;
        SelectCommand = new RelayCommand(() => select(this));
        ToggleCommand = toggle is null ? null : new AsyncRelayCommand(toggle);
        CategoryBrush = Brushes.Transparent;
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

    /// <summary>The category filter and the groups read these.</summary>
    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    /// <summary>The category's colour, "#RRGGBB".</summary>
    public string? CategoryColor { get; init; }

    /// <summary>The 3px edge at the row's left: the category's colour; transparent with none.</summary>
    public IBrush CategoryBrush { get; init; }

    /// <summary>The check box is ticked: a task that is done.</summary>
    public bool IsTicked { get; }

    /// <summary>Ticks or unticks it (the check box); the rules are Today's.</summary>
    public ICommand? ToggleCommand { get; }

    public bool HasToggle => ToggleCommand is not null;

    public ICommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Where a dragged row would land relative to this one: -1 above, 1 below, 0 none (draws a line).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLineAbove), nameof(ShowsLineBelow))]
    public partial int DropMark { get; set; }

    public bool ShowsLineAbove => DropMark < 0;

    public bool ShowsLineBelow => DropMark > 0;

    public IReadOnlyList<TaskMenuEntry> BuildMenu()
    {
        return _menu?.Invoke() ?? [];
    }
}
