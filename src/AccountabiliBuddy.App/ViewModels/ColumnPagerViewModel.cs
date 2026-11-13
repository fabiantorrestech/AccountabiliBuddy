using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The page bar under the Shelf's columns (round 2 PG1, owner 2026-11-14: "when we have more labels than what can fit on the page, let's
/// page them"). It knows the columns (their keys, in order) and how many fit one page (the panel reports that from its width), keeps the
/// column the page was turned to (<see cref="FirstIndex"/>, so a resize keeps that column in view) and remembers it per grouping for the
/// session: this PC, in memory only, never synced or stored. The numbers are <see cref="TaskColumnPaging"/>'s.
/// </summary>
public sealed partial class ColumnPagerViewModel : ObservableObject
{
    private readonly Dictionary<string, int> _firstByGrouping = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _keys = [];
    private string _grouping = string.Empty;
    private string? _revealKey;
    private int _perPage = 1;

    public ColumnPagerViewModel()
    {
        PreviousCommand = new RelayCommand(() => Step(-1), () => PageIndex > 0);
        NextCommand = new RelayCommand(() => Step(1), () => PageIndex < PageCount - 1);
        Dots = [];
    }

    public IRelayCommand PreviousCommand { get; }

    public IRelayCommand NextCommand { get; }

    /// <summary>The column the page was turned to: the page holding it shows (the panel reads this).</summary>
    [ObservableProperty]
    public partial int FirstIndex { get; set; }

    public int ColumnCount => _keys.Count;

    /// <summary>How many columns fit a page; the panel writes it as it measures.</summary>
    public int PerPage
    {
        get => _perPage;
        set => SetPerPage(value);
    }

    public int PageCount => TaskColumnPaging.PageCount(_keys.Count, _perPage);

    /// <summary>The page showing now, 0-based.</summary>
    public int PageIndex => TaskColumnPaging.PageOf(FirstIndex, _keys.Count, _perPage);

    /// <summary>The columns do not all fit: the bar shows.</summary>
    public bool HasPages => PageCount > 1;

    public bool ShowDots => HasPages && !TaskColumnPaging.UsesCount(PageCount);

    public bool ShowCount => HasPages && TaskColumnPaging.UsesCount(PageCount);

    /// <summary>"2 / 9".</summary>
    public string CountText => $"{PageIndex + 1} / {PageCount}";

    /// <summary>"Page 2 of 4", for the bar's accessible name.</summary>
    public string SummaryText => $"Page {PageIndex + 1} of {PageCount}";

    [ObservableProperty]
    public partial IReadOnlyList<ColumnPagerDotViewModel> Dots { get; set; }

    /// <summary>The columns of the page showing now, in order.</summary>
    public IReadOnlyList<string> KeysOnPage => PageKeys(PageIndex);

    /// <summary>The columns of page <paramref name="page"/>, in order.</summary>
    public IReadOnlyList<string> PageKeys(int page)
    {
        int first = TaskColumnPaging.FirstOfPage(page, _keys.Count, _perPage);
        return _keys.Skip(first).Take(_perPage).ToList();
    }

    /// <summary>
    /// The columns now shown (after the page was rebuilt): <paramref name="grouping"/> is the Group by ("bins", "label", "tag", "none").
    /// A new grouping finds the page last left in it; a column asked for by <see cref="Reveal"/> is paged to.
    /// </summary>
    public void Use(string grouping, IReadOnlyList<string> keys)
    {
        _keys = keys;
        int first = FirstIndex;
        if (!string.Equals(grouping, _grouping, StringComparison.Ordinal))
        {
            _grouping = grouping;
            first = _firstByGrouping.GetValueOrDefault(grouping);
        }

        if (_revealKey is not null)
        {
            int at = keys.ToList().IndexOf(_revealKey);
            if (at >= 0)
            {
                first = TaskColumnPaging.FirstOfPage(TaskColumnPaging.PageOf(at, keys.Count, _perPage), keys.Count, _perPage);
            }

            _revealKey = null;
        }

        SetFirst(Math.Clamp(first, 0, Math.Max(0, keys.Count - 1)));
        Refresh();
    }

    /// <summary>The panel measured: this many columns fit a page now.</summary>
    public void SetPerPage(int perPage)
    {
        perPage = Math.Max(1, perPage);
        if (perPage == _perPage)
        {
            return;
        }

        _perPage = perPage;
        Refresh();
    }

    /// <summary>The next <see cref="Use"/> turns to the page holding the column <paramref name="key"/> (it moved across a page edge).</summary>
    public void Reveal(string key) => _revealKey = key;

    /// <summary>Turns to page <paramref name="page"/> (0-based, kept inside the pages); false when it is the page showing.</summary>
    public bool GoToPage(int page)
    {
        int target = Math.Clamp(page, 0, PageCount - 1);
        if (target == PageIndex)
        {
            return false;
        }

        SetFirst(TaskColumnPaging.FirstOfPage(target, _keys.Count, _perPage));
        Refresh();
        return true;
    }

    /// <summary>One page on (1) or back (-1); false at an end.</summary>
    public bool Step(int delta) => GoToPage(PageIndex + delta);

    /// <summary>The page that holds the column <paramref name="key"/>, or -1.</summary>
    public int PageOfKey(string key)
    {
        int at = _keys.ToList().IndexOf(key);
        return at < 0 ? -1 : TaskColumnPaging.PageOf(at, _keys.Count, _perPage);
    }

    private void SetFirst(int first)
    {
        FirstIndex = first;
        if (_grouping.Length > 0)
        {
            _firstByGrouping[_grouping] = first;
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(ColumnCount));
        OnPropertyChanged(nameof(PerPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageIndex));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(ShowDots));
        OnPropertyChanged(nameof(ShowCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(KeysOnPage));
        int current = PageIndex;
        Dots = HasPages && !TaskColumnPaging.UsesCount(PageCount)
            ? Enumerable.Range(0, PageCount).Select(page => new ColumnPagerDotViewModel(page == current, $"Page {page + 1}", new RelayCommand(() => GoToPage(page)))).ToList()
            : [];
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>One dot of the page bar: a tap turns to its page.</summary>
public sealed record ColumnPagerDotViewModel(bool IsCurrent, string Tip, IRelayCommand Command);
