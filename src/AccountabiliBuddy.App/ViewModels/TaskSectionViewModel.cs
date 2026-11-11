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
    private Func<string, Task>? _add;

    public TaskSectionViewModel(TaskSectionsPanelViewModel panel, string key, ObservableCollection<TaskListRowViewModel> rows)
    {
        _panel = panel;
        Key = key;
        Rows = rows;
        Title = Hint = AddText = AddTip = string.Empty;
        Rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>Its identity in the panel: "session", "queued", "reminders", "done", or a Shelf group's key ("bin:later", "label:…").</summary>
    public string Key { get; }

    public ObservableCollection<TaskListRowViewModel> Rows { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>The faint text after the title: "3 ready", "3/3 in focus", a count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    public partial string Hint { get; set; }

    public bool HasHint => Hint.Length > 0;

    /// <summary>An icon before the title (the bell of Reminders due today); null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(IconKind))]
    public partial MaterialIconKind? Icon { get; set; }

    public bool HasIcon => Icon is not null;

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

    /// <summary>
    /// Drops a dragged row before or after another of this section (This session's order); null where rows don't drag.
    /// </summary>
    public Func<Guid, Guid, bool, Task>? MoveRow { get; set; }

    // ---- The "+" of a Shelf bin: a box opens in the section; Enter adds a task there. ----

    /// <summary>The header has "+" (a Shelf bin).</summary>
    public bool HasAdd => _add is not null;

    /// <summary>E.g. "Add to To sort".</summary>
    [ObservableProperty]
    public partial string AddTip { get; set; }

    [ObservableProperty]
    public partial bool IsAdding { get; set; }

    [ObservableProperty]
    public partial string AddText { get; set; }

    /// <summary>Gives the header its "+": <paramref name="add"/> adds a typed title to this section's bin.</summary>
    public void UseAdd(Func<string, Task>? add, string tip)
    {
        _add = add;
        AddTip = tip;
        OnPropertyChanged(nameof(HasAdd));
    }

    [RelayCommand]
    private void BeginAdd()
    {
        if (_add is null)
        {
            return;
        }

        AddText = string.Empty;
        IsAdding = true;
        if (!IsOpen)
        {
            _panel.Toggle(this);
        }
    }

    [RelayCommand]
    private async Task CommitAddAsync()
    {
        string title = AddText.Trim();
        AddText = string.Empty;
        if (title.Length > 0 && _add is not null)
        {
            await _add(title);
        }
    }

    [RelayCommand]
    private void CancelAdd()
    {
        AddText = string.Empty;
        IsAdding = false;
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
