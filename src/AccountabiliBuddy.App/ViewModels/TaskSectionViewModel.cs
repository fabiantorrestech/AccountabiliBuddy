using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One section of a task panel (Today: This session, Queued, Reminders due today, Finished today; the Shelf: To sort, Next, Later or a
/// label / tag group): a header that folds it (chevron, optional icon or label dot, title, a faint hint such as "3 ready", a pin when
/// pinned open, a "+" on a Shelf bin), then its rows, or "Nothing to see here." when it has none. The rows are a list the page
/// rebuilds in place (<see cref="TasksViewModel"/>), so the same rows can show in two panels (the Tasks page and Today) at once.
/// </summary>
public sealed partial class TaskSectionViewModel : ObservableObject
{
    private readonly TaskSectionsPanelViewModel _panel;

    public TaskSectionViewModel(TaskSectionsPanelViewModel panel, string key, ObservableCollection<TaskListRowViewModel> rows)
    {
        _panel = panel;
        Key = key;
        Rows = rows;
        Title = Hint = AddTip = string.Empty;
        Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>Its identity in the panel: "session", "queued", "reminders", "done", or a Shelf group's key ("bin:later", "label:…").</summary>
    public string Key { get; }

    public ObservableCollection<TaskListRowViewModel> Rows { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailTitle))]
    public partial string Title { get; set; }

    /// <summary>The faint text after the title: "3 ready", "3/3 in focus", a count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint), nameof(ShowsCountPill), nameof(ShowsHintText))]
    public partial string Hint { get; set; }

    public bool HasHint => Hint.Length > 0;

    /// <summary>
    /// A Shelf group (a bin, a label or a tag; not one of Today's sections): its header follows the shared section header rule (round 2,
    /// brief §3.4): 16.5 px bold title, 19 px icon, the count in a grey pill, "+" only on hover or keyboard focus.
    /// </summary>
    public bool IsShelfGroup => Key is not ("session" or "queued" or "reminders" or "done");

    /// <summary>This session (round 2: Today draws it in its own card, the folded sections under it).</summary>
    public bool IsSessionSection => Key == "session";

    /// <summary>A Shelf group's count, in its grey pill.</summary>
    public bool ShowsCountPill => IsShelfGroup && HasHint;

    /// <summary>One of Today's sections: its hint ("3 ready", a count) as faint text after the title.</summary>
    public bool ShowsHintText => !IsShelfGroup && HasHint;

    /// <summary>An icon before the title (the bell of Reminders due today); null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(IconKind), nameof(ShowsSectionIcon), nameof(ShowsSmallIcon))]
    public partial MaterialIconKind? Icon { get; set; }

    public bool HasIcon => Icon is not null;

    /// <summary>The icon in the shared section header size (a Shelf bin).</summary>
    public bool ShowsSectionIcon => IsShelfGroup && HasIcon;

    /// <summary>The icon in Today's smaller section header (Reminders due today's bell).</summary>
    public bool ShowsSmallIcon => !IsShelfGroup && HasIcon;

    public MaterialIconKind IconKind => Icon ?? MaterialIconKind.BellOutline;

    /// <summary>A label group's colour dot; null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDot))]
    public partial IBrush? Dot { get; set; }

    public bool HasDot => Dot is not null;

    /// <summary>Quieter than This session (Queued, Reminders due today, Finished today): faded, its header in the faint colour.</summary>
    public bool IsQuiet { get; init; }

    /// <summary>It can be pinned open (Today's sections).</summary>
    public bool CanPin { get; init; }

    /// <summary>Open unless the panel says otherwise (Today's quiet sections start folded).</summary>
    public bool OpensByDefault { get; init; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron))]
    public partial bool IsOpen { get; set; }

    public MaterialIconKind Chevron => IsOpen ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPin))]
    public partial bool IsPinned { get; set; }

    public bool ShowsPin => CanPin && IsPinned;

    /// <summary>The whole section's opacity: 0.66 for a quiet one (0.40 while a session runs), else 1.</summary>
    [ObservableProperty]
    public partial double Fade { get; set; } = 1;

    /// <summary>Compact rows (32 DIP).</summary>
    [ObservableProperty]
    public partial bool IsCompact { get; set; }

    public bool IsEmpty => Rows.Count == 0;

    public bool ShowsRows => IsOpen && !IsEmpty;

    public bool ShowsEmpty => IsOpen && IsEmpty;

    /// <summary>What an empty open section says.</summary>
    public const string DefaultEmptyText = "Nothing to see here.";

    /// <summary>"Nothing to see here.", or "Nothing with these labels here." in a Shelf column while the label row filters (Tasks redesign 4b.15).</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = DefaultEmptyText;

    /// <summary>
    /// Tasks redesign 7.37-7.40: the search words when nothing on the Shelf matches them and this is the first column (it then shows the
    /// dashed "Search the Archive for “…”" box instead of its empty line); empty otherwise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsHandOff), nameof(ShowsEmptyLine), nameof(HandOffTitle), nameof(HandOffButtonText))]
    public partial string HandOffWords { get; set; } = string.Empty;

    /// <summary>The hand-off box's button: opens the Archive with the same words and filters (set by the page).</summary>
    public System.Windows.Input.ICommand? HandOffCommand { get; set; }

    /// <summary>The dashed hand-off box shows.</summary>
    public bool ShowsHandOff => ShowsEmpty && HandOffWords.Length > 0;

    /// <summary>The plain empty line shows (not while the hand-off box does).</summary>
    public bool ShowsEmptyLine => ShowsEmpty && HandOffWords.Length == 0;

    /// <summary>"Nothing on the Shelf matches “gopro”."</summary>
    public string HandOffTitle => $"Nothing on the Shelf matches “{HandOffWords}”.";

    /// <summary>"Search the Archive for “gopro”".</summary>
    public string HandOffButtonText => $"Search the Archive for “{HandOffWords}”";

    /// <summary>The bin its tasks are in (This session, Queued, Finished today, a Shelf bin); null for a label or tag group and Reminders due today.</summary>
    public AccountabiliBuddy.Core.Domain.TaskBin? Bin { get; set; }

    // ---- Round 2 R3: a Shelf group drawn as a column (its own scroll, a pinned header) ----

    /// <summary>How many of its rows are highlighted (the header's "★ 1").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStars), nameof(StarText))]
    public partial int StarCount { get; set; }

    public bool HasStars => StarCount > 0;

    public string StarText => StarCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The column header's accessible name: "Next, 4 tasks, 1 highlighted · Ctrl+Alt+← or Ctrl+Alt+→ moves the column".</summary>
    public string ColumnName
    {
        get
        {
            string count = Rows.Count == 1 ? "1 task" : Rows.Count + " tasks";
            string stars = HasStars ? $", {StarCount} highlighted" : string.Empty;
            return $"{Title}, {count}{stars}";
        }
    }

    /// <summary>A folded column's body: "Folded · 4 tasks" (the column keeps its width; a header click opens it).</summary>
    public string FoldedText => Rows.Count == 1 ? "Folded · 1 task" : $"Folded · {Rows.Count} tasks";

    /// <summary>The column is folded: its body shows <see cref="FoldedText"/> instead of its rows.</summary>
    public bool ShowsFolded => !IsOpen;

    // ---- Tasks redesign 4c: the Shelf's columns (Group by Bins folds and rails, Group by Labels and None headers) ----

    /// <summary>It can fold to a rail: a bin or a label column (a header click or Options › Layout, 0.1.154); false keeps it open (None's list).</summary>
    [ObservableProperty]
    public partial bool CanFold { get; set; } = true;

    /// <summary>Its fold is this PC's own and kept (<see cref="Services.Tasks.TaskLayoutPreferences.FoldedColumns"/>), not forgotten when Tasks is opened again.</summary>
    [ObservableProperty]
    public partial bool StoresFold { get; set; }

    /// <summary>Later while empty in Group by Bins: a 62 px drop rail ("Empty · drop a task here") instead of a full column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRail), nameof(ShowsColumn), nameof(IsFoldedRail))]
    public partial bool IsEmptyRail { get; set; }

    /// <summary>Drawn as a 62 px rail: folded, or the empty Later rail.</summary>
    public bool IsRail => IsEmptyRail || (CanFold && !IsOpen);

    /// <summary>A folded column's rail (a click unfolds it; "Folded" as its tooltip).</summary>
    public bool IsFoldedRail => IsRail && !IsEmptyRail;

    /// <summary>Drawn as a full column (its header and rows).</summary>
    public bool ShowsColumn => !IsRail;

    /// <summary>The rail's line under its name: "Empty · drop a task here" on the empty Later rail; none on a folded one (O3: "Folded" is its tooltip).</summary>
    public string RailNote => IsEmptyRail ? "Empty · drop a task here" : string.Empty;

    public bool HasRailNote => RailNote.Length > 0;

    /// <summary>The header's tooltip: what a click and Ctrl+Alt+← / → do here.</summary>
    public string HeaderTip => IsLabelColumn
        ? "Click to fold · Ctrl+Alt+← / Ctrl+Alt+→ moves the column (this PC only)"
        : CanFold ? "Click to fold · Ctrl+Alt+← / Ctrl+Alt+→ moves the column · right-click for Expand all and Collapse all" : Title;

    /// <summary>The name a rail shows: a label column's short name, else the title.</summary>
    public string RailTitle => IsLabelColumn && ShortTitle.Length > 0 ? ShortTitle : Title;

    /// <summary>"Fold Next" / "Unfold Next": the header's and the rail's accessible name.</summary>
    public string FoldName => (IsOpen ? "Fold " : "Unfold ") + Title;

    /// <summary>Its share of the width among the open columns (Later 0.8 against the 1.0 Next and To sort share; a label column 1).</summary>
    [ObservableProperty]
    public partial double ColumnWeight { get; set; } = 1;

    /// <summary>Next ("lead") or To sort ("trail"): their widths follow the Shelf's split (the resize handle between them); empty for the others.</summary>
    [ObservableProperty]
    public partial string SplitRole { get; set; } = string.Empty;

    /// <summary>A label column of Group by Labels: a 10 px dot, the label's short name and its count at the right.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBinHeader), nameof(HeaderTip), nameof(RailTitle))]
    public partial bool IsLabelColumn { get; set; }

    /// <summary>The usual Shelf header (icon, title, count, stars, "+"): Group by Bins and None.</summary>
    public bool ShowsBinHeader => !IsLabelColumn;

    /// <summary>A label column's title: the label's name without its category ("AI" under Work).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RailTitle))]
    public partial string ShortTitle { get; set; } = string.Empty;

    /// <summary>A quiet note at the header's right ("Newest first" on Group by None's list); empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeaderNote))]
    public partial string HeaderNote { get; set; } = string.Empty;

    public bool HasHeaderNote => HeaderNote.Length > 0;

    /// <summary>The count in the header's pill: the tasks listed after filters and search ("0" too, on a rail or a label column).</summary>
    public string CountText => Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The header's grip drags the column onto another (Group by Bins); false in Group by Labels, whose order is the categories'.</summary>
    [ObservableProperty]
    public partial bool CanDragColumn { get; set; }

    /// <summary>The columns can be reordered (Group by Bins, Label or Tag with two or more columns; not None's single list).</summary>
    [ObservableProperty]
    public partial bool CanMoveColumn { get; set; }

    /// <summary>Another column's grip is being dragged over this one: letting go puts that column here.</summary>
    [ObservableProperty]
    public partial bool IsColumnDropTarget { get; set; }

    /// <summary>Ctrl+Alt+←/→ on the header: the column moves one place left or right (remembered on this PC).</summary>
    public Task MoveColumnAsync(int step) => _panel.MoveColumn?.Invoke(Key, step) ?? Task.CompletedTask;

    /// <summary>The header's grip let go over another column: this column takes that one's place.</summary>
    public Task MoveColumnOntoAsync(string targetKey) => _panel.MoveColumnOnto?.Invoke(Key, targetKey) ?? Task.CompletedTask;

    /// <summary>The page's Today strip, which takes this panel's rows when they are dropped on it; null where there is none.</summary>
    public TodayStripViewModel? TodayStrip => _panel.TodayStrip;

    /// <summary>Tasks redesign 8b.20: a stack is being dragged and this column takes it (dashed AccentRing edge, Accent 5% fill).</summary>
    [ObservableProperty]
    public partial bool IsStackLit { get; set; }

    /// <summary>Tasks redesign 8b.21: the stack is over this column (Accent edge and inner line, Accent 16% fill).</summary>
    [ObservableProperty]
    public partial bool IsStackHot { get; set; }

    /// <summary>A row is being dragged over the end of this section (a line under its last row, or under the header when empty).</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>A row dropped (or moved with Alt+↑/↓) here, from this section or another: the page decides what it means.</summary>
    public Task DropAsync(TaskRowDrop drop) => _panel.DropAsync(drop);

    /// <summary>A click on one of its rows, with the modifier keys (the panel knows the visible order for Shift+click).</summary>
    public void Click(TaskListRowViewModel row, TaskRowClick click) => _panel.Click(row, click);

    /// <summary>Ctrl+A in this section: every row the panel shows is selected.</summary>
    public void SelectAll() => _panel.SelectAllIn(this);

    /// <summary>Esc in this section: the selection is cleared. False when nothing was selected.</summary>
    public bool ClearSelection() => _panel.ClearSelection();

    // ---- The "+" of a Shelf bin: the add box opens in the section, its Send to preset to the bin. ----

    /// <summary>The header has "+" (a Shelf bin).</summary>
    public bool HasAdd => AddBox is not null;

    /// <summary>The section's add box (<see cref="AddBoxKind.Bin"/>); null when the header has no "+".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdd))]
    public partial AddBoxViewModel? AddBox { get; private set; }

    /// <summary>E.g. "Add to To sort".</summary>
    [ObservableProperty]
    public partial string AddTip { get; set; }

    [ObservableProperty]
    public partial bool IsAdding { get; set; }

    /// <summary>
    /// Gives the header its "+": the add box of <paramref name="host"/> preset to <paramref name="bin"/> (kept while the bin stays the
    /// same, so a rebuild leaves what is typed alone); null takes it away.
    /// </summary>
    public void UseAdd(IAddBoxHost? host, AccountabiliBuddy.Core.Domain.TaskBin bin, string tip)
    {
        AddTip = tip;
        if (host is null)
        {
            DropAddBox();
            return;
        }

        if (AddBox is { } held && held.PresetDestination == bin)
        {
            return;
        }

        DropAddBox();
        var box = new AddBoxViewModel(host, AddBoxKind.Bin, bin);
        box.Closed += OnAddBoxClosed;
        AddBox = box;
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (AddBox is null)
        {
            return;
        }

        IsAdding = true;
        if (!IsOpen)
        {
            _panel.Toggle(this);
        }

        AddBox.Open();
    }

    private void OnAddBoxClosed(object? sender, EventArgs e)
    {
        IsAdding = false;
    }

    private void DropAddBox()
    {
        if (AddBox is { } old)
        {
            old.Closed -= OnAddBoxClosed;
            AddBox = null;
            IsAdding = false;
        }
    }

    /// <summary>A click on the header: fold or unfold it (until Tasks is next opened).</summary>
    [RelayCommand]
    private void Toggle() => _panel.Toggle(this);

    /// <summary>A right-click on the header: Expand all, Collapse all and the Pinned open switches.</summary>
    public IReadOnlyList<TaskMenuEntry> HeaderMenu() => _panel.HeaderMenu();

    partial void OnIsOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsRows));
        OnPropertyChanged(nameof(ShowsEmpty));
        OnPropertyChanged(nameof(ShowsHandOff));
        OnPropertyChanged(nameof(ShowsEmptyLine));
        OnPropertyChanged(nameof(ShowsFolded));
        OnRailChanged();
    }

    partial void OnCanFoldChanged(bool value)
    {
        OnRailChanged();
        OnPropertyChanged(nameof(HeaderTip));
    }

    partial void OnIsEmptyRailChanged(bool value) => OnPropertyChanged(nameof(RailNote));

    private void OnRailChanged()
    {
        OnPropertyChanged(nameof(IsRail));
        OnPropertyChanged(nameof(IsFoldedRail));
        OnPropertyChanged(nameof(ShowsColumn));
        OnPropertyChanged(nameof(FoldName));
        OnPropertyChanged(nameof(HasRailNote));
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(ColumnName));
        OnPropertyChanged(nameof(FoldName));
    }

    partial void OnStarCountChanged(int value)
    {
        OnPropertyChanged(nameof(ColumnName));
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowsRows));
        OnPropertyChanged(nameof(ShowsEmpty));
        OnPropertyChanged(nameof(ShowsHandOff));
        OnPropertyChanged(nameof(ShowsEmptyLine));
        OnPropertyChanged(nameof(FoldedText));
        OnPropertyChanged(nameof(ColumnName));
        OnPropertyChanged(nameof(CountText));
    }
}
