using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One app or site in a History top list, with a bar relative to the list's largest entry, its icon (or a letter
/// badge), and a ⋯ menu: rename, hide from the list (time still counts), or delete from the period shown (time no
/// longer counts; asks first, can be restored). A browser's row holds its sites, and a Reddit, YouTube or GitHub row
/// its subreddits, channels or repos (ADR 17): the top few show under it, the rest on request.
/// </summary>
public sealed partial class UsageRowViewModel : ObservableObject
{
    private static readonly Color[] BadgeColors =
    {
        Color.Parse("#0F766E"), Color.Parse("#2563EB"), Color.Parse("#9333EA"), Color.Parse("#DB2777"),
        Color.Parse("#C2410C"), Color.Parse("#4D7C0F"), Color.Parse("#475569"), Color.Parse("#B45309"),
    };

    // Sites shown under a browser before "+N more".
    private const int SitesShown = 3;

    private readonly Func<UsageRowViewModel, string?, Task> _rename;
    private readonly Func<UsageRowViewModel, Task>? _hide;
    private readonly Func<Task>? _delete;
    private readonly string _periodText;
    private readonly IReadOnlyList<UsageRowViewModel> _sites;
    private readonly Func<Task<bool>>? _showFile;
    private readonly Action? _openSite;
    private readonly (string One, string Many) _childNoun;

    /// <param name="realNames">The process names or domains this row adds up (several when they share a display name).</param>
    /// <param name="fraction">0–1, this entry's time divided by the top entry's time.</param>
    /// <param name="rename">Saves a new display name for <paramref name="realNames"/> (null = back to the real name).</param>
    /// <param name="sites">
    /// For a browser: its sites; for a site with sections: its subreddits, channels or repos. Most time first, with bars
    /// relative to this row's time.
    /// </param>
    /// <param name="hide">Hides the row from its list; null when the list can't hide rows.</param>
    /// <param name="delete">Deletes the row's time in the period shown; null when it can't be deleted.</param>
    /// <param name="periodText">The period shown, for the delete question (e.g. "today").</param>
    /// <param name="isDistraction">In "Top distractions" (orange bar).</param>
    /// <param name="showFile">For an app: shows its program file in File Explorer; false when it can't be found.</param>
    /// <param name="openSite">For a website: opens it in the default browser.</param>
    /// <param name="childNoun">What <paramref name="sites"/> are called, for "+3 more subreddits"; sites by default.</param>
    public UsageRowViewModel(
        IReadOnlyList<string> realNames,
        string name,
        string timeText,
        double fraction,
        Bitmap? icon,
        Func<UsageRowViewModel, string?, Task> rename,
        IReadOnlyList<UsageRowViewModel>? sites = null,
        Func<UsageRowViewModel, Task>? hide = null,
        Func<Task>? delete = null,
        string periodText = "",
        bool isDistraction = false,
        Func<Task<bool>>? showFile = null,
        Action? openSite = null,
        (string One, string Many)? childNoun = null)
    {
        _childNoun = childNoun ?? ("site", "sites");
        _showFile = showFile;
        _openSite = openSite;
        FileNotFoundText = string.Empty;
        _hide = hide;
        _delete = delete;
        _periodText = periodText;
        IsDistraction = isDistraction;
        _sites = sites ?? Array.Empty<UsageRowViewModel>();
        foreach (UsageRowViewModel site in _sites.Take(SitesShown))
        {
            ShownSites.Add(site);
        }

        RealNames = realNames;
        Name = name;
        TimeText = timeText;
        Fraction = fraction;
        Icon = icon;
        _rename = rename;
        EditText = name;

        string shown = name.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
        // "r/MouseReview" → M and "dotnet/runtime" → R, so a site's sections don't all share one letter.
        shown = shown[(shown.LastIndexOf('/') + 1)..];
        char first = shown.FirstOrDefault(char.IsLetterOrDigit);
        BadgeText = first == default ? "?" : char.ToUpperInvariant(first).ToString();
        int hash = name.ToLowerInvariant().Aggregate(17, (sum, c) => unchecked((sum * 31) + c));
        BadgeBrush = new SolidColorBrush(BadgeColors[(int)((uint)hash % BadgeColors.Length)]);
    }

    public IReadOnlyList<string> RealNames { get; }

    public string Name { get; }

    public string TimeText { get; }

    public double Fraction { get; }

    /// <summary>E.g. "us.nothing.tech" when renamed to something else; empty otherwise.</summary>
    public string RealNameHint => RealNames.All(real => real.Equals(Name, StringComparison.OrdinalIgnoreCase))
        ? string.Empty
        : string.Join(", ", RealNames.Distinct(StringComparer.OrdinalIgnoreCase));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial Bitmap? Icon { get; set; }

    public bool HasIcon => Icon is not null;

    public string BadgeText { get; }

    public IBrush BadgeBrush { get; }

    /// <summary>The sites (or sections) shown under this row (the top few, or all).</summary>
    public ObservableCollection<UsageRowViewModel> ShownSites { get; } = new();

    /// <summary>Every site (or section) under this row, shown or not.</summary>
    public IReadOnlyList<UsageRowViewModel> Sites => _sites;

    public bool HasSites => _sites.Count > 0;

    public bool CanHide => _hide is not null;

    public bool CanDelete => _delete is not null;

    public bool CanShowFile => _showFile is not null;

    public bool CanOpenSite => _openSite is not null;

    /// <summary>Shown under the name for a while when the app's program file can't be found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFileNotFound))]
    public partial string FileNotFoundText { get; set; }

    public bool ShowsFileNotFound => FileNotFoundText.Length > 0;

    public bool IsDistraction { get; }

    /// <summary>E.g. "Delete 1 h 3 min of brave from today?"</summary>
    public string DeleteQuestion => $"Delete {TimeText} of {Name} from {_periodText}? It won't count anywhere (you can restore it).";

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public bool HasMoreSites => _sites.Count > SitesShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SitesToggleText))]
    public partial bool ShowsAllSites { get; set; }

    public string SitesToggleText => ShowsAllSites ? $"Fewer {_childNoun.Many}"
        : _sites.Count - SitesShown == 1 ? $"+1 more {_childNoun.One}" : $"+{_sites.Count - SitesShown} more {_childNoun.Many}";

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; }

    [RelayCommand]
    private void ToggleSites()
    {
        ShowsAllSites = !ShowsAllSites;
        ShownSites.Clear();
        foreach (UsageRowViewModel site in ShowsAllSites ? _sites : _sites.Take(SitesShown))
        {
            ShownSites.Add(site);
        }
    }

    [RelayCommand]
    private void AskDelete()
    {
        IsEditing = false;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private Task ConfirmDeleteAsync()
    {
        IsConfirmingDelete = false;
        return _delete?.Invoke() ?? Task.CompletedTask;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private async Task ShowInExplorerAsync()
    {
        if (_showFile is null)
        {
            return;
        }

        FileNotFoundText = await _showFile()
            ? string.Empty
            : $"Couldn't find {Name}'s program file. Open the app once, then try again.";
    }

    [RelayCommand]
    private void OpenSite()
    {
        _openSite?.Invoke();
    }

    [RelayCommand]
    private Task HideAsync()
    {
        return _hide?.Invoke(this) ?? Task.CompletedTask;
    }

    [RelayCommand]
    private void StartRename()
    {
        EditText = Name;
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        IsEditing = false;
        await _rename(this, EditText);
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsEditing = false;
    }

    /// <summary>Goes back to the real name(s).</summary>
    [RelayCommand]
    private async Task ResetNameAsync()
    {
        IsEditing = false;
        await _rename(this, null);
    }
}
