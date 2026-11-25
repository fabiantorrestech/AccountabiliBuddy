using System.ComponentModel;
using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 4c (SPEC "Page layout" row 4): how the Shelf's columns share the width. Group by Bins: Next leads (58% of the width it
// shares with To sort by default; the handle between them resizes it, 30–75%, this PC only), Later is a 62 px rail while empty; folded
// columns and the empty Later sit at the right (0.1.154). Group by Labels: one column per label, then No label, in this PC's own order
// (0.1.154; category order until moved), as many 220 px columns a page as fit (at most 4) plus the folded ones that still fit, at that
// page's right, with a band row naming each run of a category on the page and the ‹ 1 / 3 › pager at its right. Group by None: one list
// across the width.
public sealed partial class ShelfViewModel
{
    /// <summary>Group by Labels: at most this many label columns a page.</summary>
    public const int MostLabelColumns = 4;

    /// <summary>Group by Labels: a label column is at least this wide.</summary>
    public const double LabelColumnWidth = 220;

    // Each label column's place in the band row: its category (or "Other labels" / "No label") and how many columns that band has in all.
    private readonly Dictionary<string, LabelBandInfo> _bandOf = new(StringComparer.Ordinal);
    private TaskLayoutStore? _layoutStore;
    private EventHandler? _layoutChanged;
    private PropertyChangedEventHandler? _pagerChanged;

    // LabelColumns brought the label row up to date in this rebuild: the Refresh right after it needn't do it again.
    private bool _labelRowSynced;

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
        _layoutChanged = (_, _) =>
        {
            ShelfSplit = layout.Current.ShelfSplitNow;
            // A fold moves a label column to its page's right: the bands follow the order drawn.
            RebuildBands();
        };
        layout.Changed += _layoutChanged;
        _pagerChanged = (_, args) =>
        {
            if (args.PropertyName is nameof(ColumnPagerViewModel.KeysOnPage))
            {
                RebuildBands();
                OnPropertyChanged(nameof(LabelPageText));
                OnPropertyChanged(nameof(ShowsBottomPager));
            }
        };
        Pager.PropertyChanged += _pagerChanged;
    }

    /// <summary>Lets go of the layout store and the pager (<see cref="FollowLayout"/>, <see cref="FollowFade"/>); the page's dispose calls it.</summary>
    public void Detach()
    {
        if (_layoutStore is { } layout)
        {
            if (_layoutChanged is not null)
            {
                layout.Changed -= _layoutChanged;
            }

            if (_fadeChanged is not null)
            {
                layout.Changed -= _fadeChanged;
            }
        }

        if (_pagerChanged is not null)
        {
            Pager.PropertyChanged -= _pagerChanged;
        }

        _layoutChanged = null;
        _fadeChanged = null;
        _pagerChanged = null;
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
    /// its full and short name, colour and band. The label row is brought up to date first, so both read the same order. The page then
    /// puts them in this PC's own order (0.1.154), so this order only places the columns it doesn't name yet.
    /// </summary>
    public IReadOnlyList<LabelColumnSpec> LabelColumns()
    {
        SyncLabelRow();
        _labelRowSynced = true;

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

    // The bands of the page showing: one per run of a category in the order drawn (the page's open columns, then its rails), spanning its
    // columns; "4 of 7 on this page" when not all its labels are here.
    private void RebuildBands()
    {
        var bands = new List<LabelBandViewModel>();
        var onPage = new Dictionary<string, int>(StringComparer.Ordinal);
        if (IsGroupedByLabels)
        {
            List<string> drawn = TaskColumnOrder.OpenFirst(
                Pager.KeysOnPage, key => Panel.Sections.FirstOrDefault(section => section.Key == key)?.IsRail ?? false);
            foreach (string key in drawn)
            {
                if (!_bandOf.TryGetValue(key, out LabelBandInfo info))
                {
                    continue;
                }

                onPage[info.Key] = onPage.GetValueOrDefault(info.Key) + 1;
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
            band.Finish(onPage.GetValueOrDefault(band.Key, band.Span));
            LabelBands.Add(band);
        }

        OnPropertyChanged(nameof(LabelPageText));
    }

    /// <summary>Group by Labels: the category icon and name of a label column (Options › Layout's rows); null when it has none.</summary>
    public (MaterialIconKind Icon, string Name)? BandOf(string key) =>
        _bandOf.TryGetValue(key, out LabelBandInfo info) ? (info.Icon, info.Name) : null;

    private readonly record struct LabelBandInfo(string Key, string Name, MaterialIconKind Icon, int Total);
}
