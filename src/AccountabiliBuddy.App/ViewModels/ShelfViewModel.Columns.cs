using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 4c (SPEC "Page layout" row 4): how the Shelf's columns share the width. Group by Bins: Next leads (58% of the width it
// shares with To sort by default; the handle between them resizes it, 30–75%, this PC only), Later is a 62 px rail while empty. Group by
// Labels: one column per label in category order, then No label, as many 220 px columns a page as fit (at most 4), with a band row
// naming each category on the page and the ‹ 1 / 3 › pager at its right. Group by None: one list across the width.
public sealed partial class ShelfViewModel
{
    /// <summary>Group by Labels: at most this many label columns a page.</summary>
    public const int MostLabelColumns = 4;

    /// <summary>Group by Labels: a label column is at least this wide.</summary>
    public const double LabelColumnWidth = 220;

    // Each label column's place in the band row: its category (or "Other labels" / "No label") and how many columns that band has in all.
    private readonly Dictionary<string, LabelBandInfo> _bandOf = new(StringComparer.Ordinal);
    private TaskLayoutStore? _layoutStore;

    /// <summary>Next's share of the width it shares with To sort (0.30–0.75): this PC's split, or the one being dragged.</summary>
    [ObservableProperty]
    public partial double ShelfSplit { get; set; } = TaskLayoutPreferences.DefaultShelfSplit / 100.0;

    /// <summary>The narrowest a column may be: 220 for label columns, else 300 (the three bins may still shrink to 220).</summary>
    public double ColumnMinWidth => IsGroupedByLabels ? LabelColumnWidth : 300;

    /// <summary>The most columns a page shows: 4 in Group by Labels; 0 (no limit) otherwise.</summary>
    public int ColumnsPerPageLimit => IsGroupedByLabels ? MostLabelColumns : 0;

    /// <summary>The page bar under the columns (Bins, None); Group by Labels pages from its band row instead.</summary>
    public bool ShowsBottomPager => !IsGroupedByLabels && Pager.HasPages;

    /// <summary>Group by Labels' band row: the categories of the page showing now, each spanning its columns.</summary>
    public ObservableCollection<LabelBandViewModel> LabelBands { get; } = [];

    /// <summary>"1 / 3": the Labels view's page.</summary>
    public string LabelPageText => $"{Pager.PageIndex + 1} / {Pager.PageCount}";

    /// <summary>The split and the column folds follow this PC's Tasks layout.</summary>
    public void FollowLayout(TaskLayoutStore layout)
    {
        _layoutStore = layout;
        ShelfSplit = layout.Current.ShelfSplitNow;
        layout.Changed += (_, _) => ShelfSplit = layout.Current.ShelfSplitNow;
        Pager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ColumnPagerViewModel.KeysOnPage))
            {
                RebuildBands();
                OnPropertyChanged(nameof(LabelPageText));
                OnPropertyChanged(nameof(ShowsBottomPager));
            }
        };
    }

    /// <summary>The handle between Next and To sort was let go (or moved with ← / →): Next's new share is kept on this PC.</summary>
    public Task SaveShelfSplitAsync(double share)
    {
        double clamped = Math.Clamp(share, TaskLayoutPreferences.MinShelfSplit / 100.0, TaskLayoutPreferences.MaxShelfSplit / 100.0);
        ShelfSplit = clamped;
        return _layoutStore?.SetShelfSplitAsync((int)Math.Round(clamped * 100)) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Group by Labels' columns, in the +N menu's order (category by category, then "Other labels"), then No label: each label's key,
    /// its full and short name, colour and band. The label row is brought up to date first, so both read the same order.
    /// </summary>
    public IReadOnlyList<LabelColumnSpec> LabelColumns()
    {
        SyncLabelRow();

        // With labels picked in the label row, only their columns show (No label only when it is picked); the bands count what shows.
        bool picking = IsNoLabelPicked || MenuGroups.Any(group => group.Items.Any(chip => chip.IsSelected));
        var specs = new List<LabelColumnSpec>();
        foreach (LabelFilterGroupViewModel group in MenuGroups)
        {
            string band = group.GroupId?.ToString("D") ?? "other";
            List<LabelFilterChipViewModel> shown = group.Items.Where(chip => !picking || chip.IsSelected).ToList();
            foreach (LabelFilterChipViewModel chip in shown)
            {
                specs.Add(new LabelColumnSpec("label:" + chip.Id.ToString("D"), chip.Id, chip.Name, chip.ShortName, chip.Dot, band, group.Name, group.Icon, shown.Count));
            }
        }

        if (!picking || IsNoLabelPicked)
        {
            specs.Add(new LabelColumnSpec("label:none", null, "No label", "No label", null, "none", "No label", MaterialIconKind.CircleOffOutline, 1));
        }

        return specs;
    }

    /// <summary>The label columns now shown (Group by Labels): the band row follows them; empty for the other groupings.</summary>
    public void UseLabelBands(IReadOnlyList<LabelColumnSpec> columns)
    {
        _bandOf.Clear();
        foreach (LabelColumnSpec column in columns)
        {
            _bandOf[column.Key] = new LabelBandInfo(column.BandKey, column.BandName, column.BandIcon, column.BandTotal);
        }

        OnPropertyChanged(nameof(ColumnMinWidth));
        OnPropertyChanged(nameof(ColumnsPerPageLimit));
        OnPropertyChanged(nameof(ShowsBottomPager));
        RebuildBands();
    }

    // The bands of the page showing: one per category run, spanning its columns; "4 of 7 on this page" when not all its labels are here.
    private void RebuildBands()
    {
        var bands = new List<LabelBandViewModel>();
        if (IsGroupedByLabels)
        {
            foreach (string key in Pager.KeysOnPage)
            {
                if (!_bandOf.TryGetValue(key, out LabelBandInfo info))
                {
                    continue;
                }

                if (bands.Count > 0 && bands[^1].Key == info.Key)
                {
                    bands[^1].Span++;
                    continue;
                }

                bands.Add(new LabelBandViewModel(info.Key, info.Name, info.Icon, info.Total) { Span = 1 });
            }
        }

        LabelBands.Clear();
        foreach (LabelBandViewModel band in bands)
        {
            band.Finish();
            LabelBands.Add(band);
        }

        OnPropertyChanged(nameof(LabelPageText));
    }

    private readonly record struct LabelBandInfo(string Key, string Name, MaterialIconKind Icon, int Total);
}
