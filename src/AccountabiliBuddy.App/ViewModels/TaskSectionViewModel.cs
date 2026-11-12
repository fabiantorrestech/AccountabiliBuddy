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

    public string EmptyText => "Nothing to see here.";

    /// <summary>The bin its tasks are in (This session, Queued, Finished today, a Shelf bin); null for a label or tag group and Reminders due today.</summary>
    public AccountabiliBuddy.Core.Domain.TaskBin? Bin { get; set; }

    /// <summary>A row is being dragged over the end of this section (a line under its last row, or under the header when empty).</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>A row dropped (or moved with Alt+↑/↓) here, from this section or another: the page decides what it means.</summary>
    public Task DropAsync(TaskRowDrop drop) => _panel.DropAsync(drop);

    /// <summary>A click on one of its rows, with the modifier keys (the panel knows the visible order for Shift+click).</summary>
    public void Click(TaskListRowViewModel row, TaskRowClick click) => _panel.Click(row, click);

    /// <summary>Ctrl+A in this section: every row the panel shows is selected.</summary>
    public void SelectAll() => _panel.SelectAll();

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
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowsRows));
        OnPropertyChanged(nameof(ShowsEmpty));
    }
}
