using System.Windows.Input;
using AccountabiliBuddy.App.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One task row of the Tasks page and Today's sections (spec 03 §B): the session number or a check box, the title (a star before it
/// when highlighted, the row tinted), the detail line (⏱ time limit, 📅 due, 🔔 reminder, #tags, origin, "untouched 5 wk", the
/// sync-problem cloud), the label's dot and name, the time on This session's rows, hover quick actions (← / →), the open arrow,
/// the ⋯ menu and the drag handle. A click opens the task; Ctrl/Shift+click select (Windows rules). Ticking it strikes it through
/// for a moment, then it folds away and settles where the existing rules put it. The label is never a fill.
/// </summary>
public sealed partial class TaskListRowViewModel : ObservableObject, ITaskMenuSource
{
    // How long a ticked row stays struck through in place before it folds away, and how long the fold takes (scaled by Motion).
    private static readonly TimeSpan FinishHold = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FinishFold = TimeSpan.FromMilliseconds(300);

    private readonly Func<IReadOnlyList<TaskMenuEntry>>? _menu;
    private readonly Func<IReadOnlyList<TaskMenuEntry>>? _labelMenu;
    private readonly Action<TaskListRowViewModel, TaskRowClick> _select;
    private readonly Func<Task>? _toggle;

    public TaskListRowViewModel(
        TaskRowKey key, string title, string subText, Action<TaskListRowViewModel, TaskRowClick> select, int number = 0, bool isNow = false,
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
        _select = select;
        _toggle = toggle;
        SelectCommand = new RelayCommand(() => select(this, TaskRowClick.Open));
        // A second tick while it is still struck through takes the tick back, so the command may run while one is pending.
        ToggleCommand = toggle is null ? null : new AsyncRelayCommand(ToggleAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CategoryBrush = Brushes.Transparent;
    }

    public TaskRowKey Key { get; }

    public Guid Id => Key.Id;

    public TaskRowKind Kind => Key.Kind;

    public string Title { get; }

    /// <summary>A plain-text detail line for rows without a task-list item (e.g. "2 of 3 steps left · 50 min"); empty hides it.</summary>
    public string SubText { get; }

    public bool HasSubText => SubText.Length > 0;

    /// <summary>1, 2, 3 on This session's rows; 0 elsewhere.</summary>
    public int Number { get; }

    public bool IsNumbered => Number > 0;

    /// <summary>The running task: "Now" at its right.</summary>
    public bool IsNow { get; }

    /// <summary>The task's ID ("142"); empty when it has none yet (shown in the tooltip and the open task only).</summary>
    public string NumberText { get; init; } = string.Empty;

    /// <summary>The title's tooltip: "142 · title".</summary>
    public string Tip => NumberText.Length > 0 ? $"{NumberText} · {Title}" : Title;

    /// <summary>The detail line's pieces, in the prototype's order; empty shows <see cref="SubText"/> instead.</summary>
    public IReadOnlyList<TaskRowBit> Bits { get; init; } = [];

    public bool HasBits => Bits.Count > 0;

    public bool ShowsSubText => !HasBits && HasSubText;

    /// <summary>Highlighted: a star before the title and the accent tint (not on a finished row).</summary>
    public bool IsHighlighted { get; init; }

    /// <summary>The time on the right of a This-session row, e.g. "50 min" (number font); empty for none.</summary>
    public string TimeText { get; init; } = string.Empty;

    public bool HasTime => TimeText.Length > 0 && !IsNow;

    /// <summary>The ← quick action ("Queue it for today", or the target "Put it in this session"); null for none.</summary>
    public TaskRowAction? LeftAction { get; init; }

    /// <summary>The → quick action ("Move to the Shelf"); null for none.</summary>
    public TaskRowAction? RightAction { get; init; }

    public bool HasLeftAction => LeftAction is not null;

    public bool HasRightAction => RightAction is not null;

    /// <summary>The row can be dragged by its handle (to reorder, or to another section) and moved with Alt+↑/↓.</summary>
    public bool CanDrag { get; init; }

    /// <summary>A task of the next session's list that sits in Today's Queued (not in This session).</summary>
    public bool IsQueued { get; init; }

    public bool IsComeBack => Kind == TaskRowKind.ComeBack;

    public bool IsDone => Kind == TaskRowKind.Done;

    /// <summary>The category filter reads these.</summary>
    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    /// <summary>The label's colour, "#RRGGBB".</summary>
    public string? CategoryColor { get; init; }

    /// <summary>The label's colour, drawn as the dot before its name; transparent with none.</summary>
    public IBrush CategoryBrush { get; init; }

    /// <summary>The row shows a label dot and name (the label style's default, "dot + name").</summary>
    public bool HasCategory => !string.IsNullOrWhiteSpace(CategoryName);

    /// <summary>The row can have a label (every kind but Auto-saved): its chip, or the quiet "+ label" spot, opens the picker.</summary>
    public bool CanLabel => _labelMenu is not null;

    /// <summary>The quiet "+ label" spot: a row that can take a label and has none.</summary>
    public bool ShowsAddCategory => CanLabel && !HasCategory;

    /// <summary>One of the rows selected with Ctrl/Shift+click (the bulk actions act on these).</summary>
    [ObservableProperty]
    public partial bool IsPicked { get; set; }

    /// <summary>The check box shows where a row can be ticked.</summary>
    public bool ShowsDoneTick => HasToggle;

    /// <summary>The drag handle (it shows while the pointer is on the row).</summary>
    public bool ShowsDragHandle => CanDrag;

    /// <summary>The picker for this row's label (the same choices as ⋯ › Label), asked for each time it opens.</summary>
    public IReadOnlyList<TaskMenuEntry> BuildLabelMenu()
    {
        return _labelMenu?.Invoke() ?? [];
    }

    /// <summary>A Later item's own due date; null for every other row.</summary>
    public DateOnly? DueOn { get; init; }

    /// <summary>The row's own past-facing time (parked, started, saved, done, edited); never a due date.</summary>
    public DateTimeOffset SortTime { get; init; }

    /// <summary>A pill after the title ("Later · Fri" …); empty on the task rows of the redesign.</summary>
    public string StatusTag { get; init; } = string.Empty;

    public bool HasStatusTag => StatusTag.Length > 0;

    /// <summary>The check box is ticked: a task that is done.</summary>
    public bool IsTicked { get; }

    /// <summary>Ticked a moment ago: struck through in place until it folds away and moves (or the tick is taken back).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTicked), nameof(ShowsHighlightTint))]
    public partial bool IsFinishing { get; set; }

    /// <summary>The page's waiting ticks (strike, fold, then store); null: a tick is stored at once.</summary>
    public TaskFinishQueue? Finishes { get; init; }

    /// <summary>The ticked row is folding away (its height and opacity go to nothing).</summary>
    [ObservableProperty]
    public partial bool IsCollapsing { get; set; }

    /// <summary>The check box and the strike: ticked, or being finished.</summary>
    public bool ShowsTicked => IsTicked || IsFinishing;

    /// <summary>The accent tint behind a highlighted open row.</summary>
    public bool ShowsHighlightTint => IsHighlighted && !ShowsTicked;

    /// <summary>Ticks or unticks it (the check box); the rules are the existing ones.</summary>
    public ICommand? ToggleCommand { get; }

    public bool HasToggle => ToggleCommand is not null;

    /// <summary>Enter on the row: opens it (a click goes through <see cref="Click"/> with its modifier keys).</summary>
    public ICommand SelectCommand { get; }

    /// <summary>The row's task is the one open (its open arrow points up).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenIcon), nameof(OpenTip))]
    public partial bool IsSelected { get; set; }

    public MaterialIconKind OpenIcon => IsSelected ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown;

    public string OpenTip => IsSelected ? "Close" : "Open";

    /// <summary>Where a dragged row would land relative to this one: -1 above, 1 below, 0 none (draws a line).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLineAbove), nameof(ShowsLineBelow))]
    public partial int DropMark { get; set; }

    public bool ShowsLineAbove => DropMark < 0;

    public bool ShowsLineBelow => DropMark > 0;

    /// <summary>A click on the row with its modifier keys (Windows rules: plain opens, Ctrl toggles, Shift selects a range).</summary>
    public void Click(TaskRowClick click)
    {
        _select(this, click);
    }

    public IReadOnlyList<TaskMenuEntry> BuildMenu()
    {
        return _menu?.Invoke() ?? [];
    }

    /// <summary>The bulk menu of the page's selection (two or more rows picked); null where the page has none.</summary>
    public Func<IReadOnlyList<TaskMenuEntry>>? SelectionMenu { get; init; }

    /// <summary>
    /// Right-click on a picked row: the selection's bulk menu when two or more rows are picked, else the row's own menu.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> BuildContextMenu()
    {
        IReadOnlyList<TaskMenuEntry> bulk = IsPicked ? SelectionMenu?.Invoke() ?? [] : [];
        return bulk.Count > 0 ? bulk : BuildMenu();
    }

    /// <summary>Makes the task's Reminders &amp; due date menu (a task the task list holds); null for the other rows.</summary>
    public Func<ReminderMenuViewModel?>? ReminderMenu { get; init; }

    /// <summary>The sync-problem cloud's click: "Changed on both devices" for this task; null when it has no conflict.</summary>
    public Action? OpenConflict { get; init; }

    /// <summary>
    /// Whether <paramref name="other"/> (the same row built again) would draw the same: then the page keeps this row's object and its
    /// control instead of drawing a new one. Its commands act on the same item, so they may be kept too.
    /// </summary>
    public bool ShowsSameAs(TaskListRowViewModel other)
    {
        return Key == other.Key && Title == other.Title && SubText == other.SubText && Number == other.Number && IsNow == other.IsNow
            && IsTicked == other.IsTicked && CategoryId == other.CategoryId && CategoryName == other.CategoryName
            && CategoryColor == other.CategoryColor && StatusTag == other.StatusTag && DueOn == other.DueOn && SortTime == other.SortTime
            && HasToggle == other.HasToggle && CanLabel == other.CanLabel && IsQueued == other.IsQueued
            && NumberText == other.NumberText && Bits.SequenceEqual(other.Bits) && IsHighlighted == other.IsHighlighted
            && TimeText == other.TimeText && CanDrag == other.CanDrag
            && TaskRowAction.SameAs(LeftAction, other.LeftAction) && TaskRowAction.SameAs(RightAction, other.RightAction);
    }

    /// <summary>The same row (same item, commands and menus) with another plain detail line and no number, for a second list (Reminders due today).</summary>
    public TaskListRowViewModel WithSubText(string subText)
    {
        return new TaskListRowViewModel(Key, Title, subText, _select, 0, false, _toggle, IsTicked, _menu, _labelMenu)
        {
            CategoryId = CategoryId,
            CategoryName = CategoryName,
            CategoryColor = CategoryColor,
            CategoryBrush = CategoryBrush,
            StatusTag = StatusTag,
            SortTime = SortTime,
            DueOn = DueOn,
            IsQueued = IsQueued,
            NumberText = NumberText,
            IsHighlighted = IsHighlighted,
            IsPicked = IsPicked,
            Finishes = Finishes,
            SelectionMenu = SelectionMenu,
            ReminderMenu = ReminderMenu,
            OpenConflict = OpenConflict,
        };
    }

    // Unticking acts at once. Ticking strikes the row through, holds it a moment, folds it away and then ticks it in the store, so
    // the rows below move up smoothly; ticking the same task again before that (this row or one rebuilt for it) takes it back and
    // nothing is stored. The wait is the page's (Finishes), keyed by the task, so quitting stores it instead of losing it.
    private async Task ToggleAsync()
    {
        if (_toggle is null)
        {
            return;
        }

        if (IsTicked || Finishes is null)
        {
            await _toggle();
            return;
        }

        // The page's queue strikes, folds and resets every row of this task (this one, a rebuilt one, the Today page's).
        if (!Finishes.Cancel(Key))
        {
            await Finishes.RunAsync(Key, _toggle, FinishHold, Motion.Current?.Scale(FinishFold) ?? FinishFold);
        }
    }
}
