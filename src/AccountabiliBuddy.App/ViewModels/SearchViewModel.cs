using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Search;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The search box in the main window's top right. Typing highlights matches on the page showing (the window counts them
/// into <see cref="PageCount"/>); the dropdown offers the next match and "Search All Results…", which lists every match
/// in the app (the user's own things and the app's words) grouped by tab. Opening one hands its target to the window.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private static readonly TimeSpan CountDelay = TimeSpan.FromMilliseconds(300);

    private readonly Func<Task<IReadOnlyList<SearchEntry>>> _dataEntries;
    private readonly DispatcherTimer _countTimer;

    /// <param name="dataEntries">The user's own things as entries, built when asked (see <see cref="DataSearchSource"/>).</param>
    public SearchViewModel(Func<Task<IReadOnlyList<SearchEntry>>> dataEntries)
    {
        _dataEntries = dataEntries;
        _countTimer = new DispatcherTimer { Interval = CountDelay };
        _countTimer.Tick += (_, _) =>
        {
            _countTimer.Stop();
            _ = CountAllAsync();
        };
    }

    /// <summary>The app's words on every page (see <see cref="UiSearchSource"/>); set by the window that holds the pages.</summary>
    public Func<IReadOnlyList<SearchEntry>> UiEntries { get; set; } = () => Array.Empty<SearchEntry>();

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>Matches on the page showing, counted by the window's highlight layer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPageMatches), nameof(PageText))]
    public partial int PageCount { get; set; }

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

    public string PageText => PageCount == 1 ? "1 on this page · Enter = next, Esc = clear" : $"{PageCount} on this page · Enter = next, Esc = clear";

    public string AllText => AllCount < 0 ? "Search All Results…"
        : AllCount >= DataSearchSource.DefaultCap ? $"Search All Results… ({DataSearchSource.DefaultCap}+ in the app)"
        : $"Search All Results… ({AllCount} in the app)";

    /// <summary>Raised to move to the next match on the page.</summary>
    public event EventHandler? NextRequested;

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
        ShowsAll = false;
        Results.Clear();
        AllCount = -1;
        _countTimer.Stop();
        if (IsOpen)
        {
            _countTimer.Start();
        }
    }

    [RelayCommand]
    private void Next()
    {
        NextRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Clear()
    {
        Query = string.Empty;
    }

    [RelayCommand]
    private async Task SearchAllAsync()
    {
        string query = Query;
        IReadOnlyList<SearchEntry> found = await FindAllAsync(query);
        if (query != Query)
        {
            return;
        }

        Results.Clear();
        foreach (IGrouping<string, SearchEntry> tab in found.GroupBy(entry => entry.Path.Split(" › ")[0]))
        {
            Results.Add(new SearchResultGroup(tab.Key, tab.Select(entry => RowOf(entry, query)).ToList()));
        }

        AllCount = found.Count;
        ShowsAll = true;
        IsOpen = true;
        OnPropertyChanged(nameof(ShowsNothingFound));
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

    private async Task CountAllAsync()
    {
        string query = Query;
        IReadOnlyList<SearchEntry> found = await FindAllAsync(query);
        if (query == Query)
        {
            AllCount = found.Count;
        }
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
