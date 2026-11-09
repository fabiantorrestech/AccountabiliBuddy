using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of the Tasks page's lists (This session, then the flat list of everything else): a check
/// box, a title with a Muted line under it, a status tag, the category dot and a ⋯ menu. A click selects it for the detail card. This session's rows are
/// numbered and can be dragged. The category shows as a small dot and its name, never as a fill.
/// </summary>
public sealed partial class TaskListRowViewModel : ObservableObject, ITaskMenuSource
{
    private readonly Func<IReadOnlyList<TaskMenuEntry>>? _menu;
    private readonly Func<IReadOnlyList<TaskMenuEntry>>? _labelMenu;

    public TaskListRowViewModel(
        TaskRowKey key, string title, string subText, Action<TaskListRowViewModel> select, int number = 0, bool isNow = false,
        Func<Task>? toggle = null, bool isTicked = false, Func<IReadOnlyList<TaskMenuEntry>>? menu = null,
        Func<IReadOnlyList<TaskMenuEntry>>? labelMenu = null)
    {
        Key = key;
        Title = title;
        SubText = subText;
        Number = number;
        IsNow = isNow;
        IsTicked = isTicked;
        _menu = menu;
        _labelMenu = labelMenu;
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

    /// <summary>The category filter reads these.</summary>
    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    /// <summary>The category's colour, "#RRGGBB".</summary>
    public string? CategoryColor { get; init; }

    /// <summary>The category's colour, drawn as the dot before its name; transparent with none.</summary>
    public IBrush CategoryBrush { get; init; }

    /// <summary>The row shows a category dot and name.</summary>
    public bool HasCategory => !string.IsNullOrWhiteSpace(CategoryName);

    /// <summary>The row can have a category (every kind but Auto-saved): its chip, or the quiet "+ category" spot, opens the picker.</summary>
    public bool CanLabel => _labelMenu is not null;

    /// <summary>The quiet "+ category" spot: a row that can take a category and has none (not in select mode).</summary>
    public bool ShowsAddCategory => CanLabel && !HasCategory && !InSelectMode;

    /// <summary>Select mode: this row shows a selection circle (not the done tick) and a click on the row picks it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDoneTick), nameof(ShowsPickCircle), nameof(ShowsAddCategory), nameof(ShowsSelected), nameof(ShowsRowTools), nameof(ShowsDragHandle))]
    public partial bool InSelectMode { get; set; }

    /// <summary>Select mode: this row is one of those picked for the bulk change.</summary>
    [ObservableProperty]
    public partial bool IsPicked { get; set; }

    /// <summary>The done tick shows where a row can be ticked, except in select mode (the selection circle takes its place).</summary>
    public bool ShowsDoneTick => HasToggle && !InSelectMode;

    /// <summary>The selection circle shows in select mode on rows that can take a category.</summary>
    public bool ShowsPickCircle => InSelectMode && CanLabel;

    /// <summary>The ⋯ menu shows outside select mode (there a click means "pick", and nothing else should be one click away).</summary>
    public bool ShowsRowTools => !InSelectMode;

    /// <summary>The drag handle of This session's rows, outside select mode.</summary>
    public bool ShowsDragHandle => CanDrag && !InSelectMode;

    /// <summary>The picker for this row's category (the same choices as ⋯ › Label), asked for each time it opens.</summary>
    public IReadOnlyList<TaskMenuEntry> BuildLabelMenu()
    {
        return _labelMenu?.Invoke() ?? [];
    }

    /// <summary>A Later item's own due date; null for every other row. The flat list's due-date sorts read it.</summary>
    public DateOnly? DueOn { get; init; }

    /// <summary>The row's own past-facing time for the Most recent / Oldest sorts: when it was parked, its session started, it was saved, done or (a Later item) last edited. Never a due date, which can lie in the future.</summary>
    public DateTimeOffset SortTime { get; init; }

    /// <summary>The pill after the title on the flat list: "Come back", "Later", "Later · Fri", "Unfinished", "Auto-saved", "Done"; empty on This session's rows.</summary>
    public string StatusTag { get; init; } = string.Empty;

    public bool HasStatusTag => StatusTag.Length > 0;

    /// <summary>The check box is ticked: a task that is done.</summary>
    public bool IsTicked { get; }

    /// <summary>Ticks or unticks it (the check box); the rules are Today's.</summary>
    public ICommand? ToggleCommand { get; }

    public bool HasToggle => ToggleCommand is not null;

    public ICommand SelectCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSelected))]
    public partial bool IsSelected { get; set; }

    /// <summary>The row shows as the one in the detail card; not in select mode, where the same background means "picked".</summary>
    public bool ShowsSelected => IsSelected && !InSelectMode;

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

    /// <summary>
    /// Whether <paramref name="other"/> (the same row built again) would draw the same: then the page keeps this row's object and its
    /// control instead of drawing a new one. Its commands act on the same item, so they may be kept too.
    /// </summary>
    public bool ShowsSameAs(TaskListRowViewModel other)
    {
        return Key == other.Key && Title == other.Title && SubText == other.SubText && Number == other.Number && IsNow == other.IsNow
            && IsTicked == other.IsTicked && CategoryId == other.CategoryId && CategoryName == other.CategoryName
            && CategoryColor == other.CategoryColor && StatusTag == other.StatusTag && DueOn == other.DueOn && SortTime == other.SortTime
            && HasToggle == other.HasToggle && CanLabel == other.CanLabel;
    }
}
