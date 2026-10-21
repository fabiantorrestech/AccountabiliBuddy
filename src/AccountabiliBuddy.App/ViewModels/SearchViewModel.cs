using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Search;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The search box in the main window's top right. Typing highlights matches on the page showing (the window counts them
/// into <see cref="PageCount"/>); a moment after typing stops, the dropdown lists every match in the app (the user's own
/// things and the app's words) grouped by tab, under the line for this page's matches. Opening one hands its target to
/// the window.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly Func<Task<IReadOnlyList<SearchEntry>>> _dataEntries;
    private readonly DispatcherTimer _searchTimer;

    /// <param name="dataEntries">The user's own things as entries, built when asked (see <see cref="DataSearchSource"/>).</param>
    public SearchViewModel(Func<Task<IReadOnlyList<SearchEntry>>> dataEntries)
    {
        _dataEntries = dataEntries;
        _searchTimer = new DispatcherTimer { Interval = SearchDelay };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = FillResultsAsync();
        };
    }

    /// <summary>The app's words on every page (see <see cref="UiSearchSource"/>); set by the window that holds the pages.</summary>
    public Func<IReadOnlyList<SearchEntry>> UiEntries { get; set; } = () => Array.Empty<SearchEntry>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Matches on the page showing, counted by the window's highlight layer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPageMatches), nameof(PageText), nameof(PageCounterText))]
    public partial int PageCount { get; set; }

    /// <summary>The current (orange) match on the page, counted from 0, from the window's highlight layer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageCounterText))]
    public partial int PageIndex { get; set; }

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    /// <summary>In the box, like a browser's find bar: "2/5", or "0/0" when the page has no match.</summary>
    public string PageCounterText => PageCount == 0 ? "0/0" : $"{Math.Min(PageIndex, PageCount - 1) + 1}/{PageCount}";

    /// <summary>Matches in the whole app; -1 while not counted yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllText))]
    public partial int AllCount { get; set; } = -1;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>True when the dropdown lists all results instead of the two choices.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNothingFound))]
    public partial bool ShowsAll { get; set; }

    public bool ShowsNothingFound => ShowsAll && Results.Count == 0;

    public ObservableCollection<SearchResultGroup> Results { get; } = new();

    public bool HasPageMatches => PageCount > 0;

    public string PageText => PageCount == 1 ? "1 on this page · ↑ ↓ or Enter, Esc = clear" : $"{PageCount} on this page · ↑ ↓ or Enter, Esc = clear";

    public string AllText => AllCount < 0 ? "Search All Results…"
        : AllCount >= DataSearchSource.DefaultCap ? $"Search All Results… ({DataSearchSource.DefaultCap}+ in the app)"
        : $"Search All Results… ({AllCount} in the app)";

    /// <summary>Raised to move to the next match on the page.</summary>
    public event EventHandler? NextRequested;

    /// <summary>Raised to move to the previous match on the page.</summary>
    public event EventHandler? PreviousRequested;

    /// <summary>Raised with the result to open.</summary>
    public event EventHandler<SearchTarget>? OpenRequested;

    /// <summary>Enter: the next match on the page, or all results when the page has none.</summary>
    public void Submit()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            return;
        }

        if (HasPageMatches)
        {
            NextCommand.Execute(null);
        }
        else
        {
            _ = SearchAllCommand.ExecuteAsync(null);
        }
    }

    partial void OnQueryChanged(string value)
    {
        IsOpen = !string.IsNullOrWhiteSpace(value);
        _searchTimer.Stop();
        if (IsOpen)
        {
            // The results follow as you type; the last ones stay up until the new ones are in, so the list doesn't flicker.
            _searchTimer.Start();
            return;
        }

        ShowsAll = false;
        Results.Clear();
        AllCount = -1;
    }

    [RelayCommand]
    private void Next()
    {
        NextRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Previous()
    {
        PreviousRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Clear()
    {
        Query = string.Empty;
    }

    [RelayCommand]
    private async Task SearchAllAsync()
    {
        if (await FillResultsAsync())
        {
            IsOpen = true;
        }
    }

    // Lists every match in the app for the query; false when the query changed meanwhile. Doesn't open the dropdown:
    // after typing, a box that lost the focus in the meantime stays closed.
    private async Task<bool> FillResultsAsync()
    {
        string query = Query;
        IReadOnlyList<SearchEntry> found = await FindAllAsync(query);
        if (query != Query)
        {
            return false;
        }

        Results.Clear();
        foreach (IGrouping<string, SearchEntry> tab in found.GroupBy(entry => entry.Path.Split(" › ")[0]))
        {
            Results.Add(new SearchResultGroup(tab.Key, tab.Select(entry => RowOf(entry, query)).ToList()));
        }

        AllCount = found.Count;
        ShowsAll = true;
        OnPropertyChanged(nameof(ShowsNothingFound));
        return true;
    }

    [RelayCommand]
    private void Open(SearchResultRow? row)
    {
        if (row is null)
        {
            return;
        }

        IsOpen = false;
        OpenRequested?.Invoke(this, row.Target);
    }

    /// <summary>The user's things first, then the app's words; at most the cap in total.</summary>
    private async Task<IReadOnlyList<SearchEntry>> FindAllAsync(string query)
    {
        IReadOnlyList<SearchEntry> data = await _dataEntries();
        return DataSearchSource.Filter(data.Concat(UiEntries()), query);
    }

    private static SearchResultRow RowOf(SearchEntry entry, string query)
    {
        (int start, int length) = TextMatcher.Find(entry.Text, query).FirstOrDefault();
        return new SearchResultRow(
            entry.Text[..start],
            entry.Text.Substring(start, length),
            entry.Text[(start + length)..],
            entry.Path,
            entry.Kind,
            entry.Target);
    }
}
