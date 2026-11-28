using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One column of the Archive sheet (Tasks redesign 6.13-6.17): Unfinished, Done or Recently deleted. It shows when its pill allows it
/// (<see cref="IsShown"/>), open or folded to a 62 px rail (<see cref="IsFolded"/>; a click on the header folds it, a click on the rail
/// opens it). Its rows are <see cref="NotebookItemViewModel"/> (tasks and their steps) or <see cref="RecentlyDeletedRowViewModel"/>.
/// </summary>
public sealed partial class ArchiveColumnViewModel : ObservableObject
{
    private readonly string _emptyText;

    public ArchiveColumnViewModel(string title, MaterialIconKind iconKind, string emptyText)
    {
        Title = title;
        IconKind = iconKind;
        _emptyText = emptyText;
        EmptyText = emptyText;
    }

    public string Title { get; }

    public MaterialIconKind IconKind { get; }

    /// <summary>The rows, newest first: a task followed by its steps, or a Recently deleted line.</summary>
    public ObservableCollection<object> Rows { get; } = new();

    /// <summary>The tasks it lists now (search applied; steps not counted): the header's and the rail's count pill.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Count { get; set; }

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Its pill lets it show (Everything, or its own pill).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsColumn), nameof(ShowsRail))]
    public partial bool IsShown { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsColumn), nameof(ShowsRail), nameof(FoldName))]
    public partial bool IsFolded { get; set; }

    public bool ShowsColumn => IsShown && !IsFolded;

    public bool ShowsRail => IsShown && IsFolded;

    /// <summary>The header's and the rail's accessible name: "Fold Done" / "Unfold Recently deleted".</summary>
    public string FoldName => (IsFolded ? "Unfold " : "Fold ") + Title;

    /// <summary>The line an empty column shows ("Nothing deleted in the last 30 days."; "No matches." while searching).</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; }

    public bool IsEmpty => Rows.Count == 0;

    [RelayCommand]
    private void Toggle() => IsFolded = !IsFolded;

    /// <summary>Puts <paramref name="rows"/> in place of the old rows; <paramref name="count"/> tasks among them.</summary>
    internal void SetRows(IEnumerable<object> rows, int count, bool searching)
    {
        Rows.Clear();
        foreach (object row in rows)
        {
            Rows.Add(row);
        }

        NumberRows();

        Count = count;
        EmptyText = searching ? "No matches." : _emptyText;
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Owner 2026-11-27: each task (not its steps) and each Recently deleted line shows its place in the column, 1 at the top; the
    // numbers are as wide as the longest one, so the boxes line up.
    private void NumberRows()
    {
        int tasks = Rows.Count(row => row is RecentlyDeletedRowViewModel || row is NotebookItemViewModel { IsSubtask: false });
        double width = Math.Max(12, tasks.ToString(CultureInfo.InvariantCulture).Length * 8);
        int place = 0;
        foreach (object row in Rows)
        {
            switch (row)
            {
                case NotebookItemViewModel item:
                    item.RowNumber = item.IsSubtask ? string.Empty : (++place).ToString(CultureInfo.CurrentCulture);
                    item.NumberWidth = width;
                    break;
                case RecentlyDeletedRowViewModel deleted:
                    deleted.RowNumber = (++place).ToString(CultureInfo.CurrentCulture);
                    deleted.NumberWidth = width;
                    break;
            }
        }
    }
}
