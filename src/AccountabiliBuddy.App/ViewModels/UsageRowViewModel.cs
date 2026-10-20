using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One app or site in a History top list, with a bar relative to the list's largest entry, its icon (or a letter
/// badge), and renaming in place. A browser's row holds its sites: the top few show under it, the rest on request.
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
    private readonly IReadOnlyList<UsageRowViewModel> _sites;

    /// <param name="realNames">The process names or domains this row adds up (several when they share a display name).</param>
    /// <param name="fraction">0–1, this entry's time divided by the top entry's time.</param>
    /// <param name="rename">Saves a new display name for <paramref name="realNames"/> (null = back to the real name).</param>
    /// <param name="sites">For a browser: its sites, most time first (their bars are relative to the browser's time).</param>
    /// <param name="hide">Hides the row from its list; null when the list can't hide rows.</param>
    public UsageRowViewModel(
        IReadOnlyList<string> realNames,
        string name,
        string timeText,
        double fraction,
        Bitmap? icon,
        Func<UsageRowViewModel, string?, Task> rename,
        IReadOnlyList<UsageRowViewModel>? sites = null,
        Func<UsageRowViewModel, Task>? hide = null)
    {
        _hide = hide;
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

    /// <summary>The sites shown under this browser (the top few, or all).</summary>
    public ObservableCollection<UsageRowViewModel> ShownSites { get; } = new();

    /// <summary>Every site under this browser, shown or not.</summary>
    public IReadOnlyList<UsageRowViewModel> Sites => _sites;

    public bool HasSites => _sites.Count > 0;

    public bool CanHide => _hide is not null;

    public bool HasMoreSites => _sites.Count > SitesShown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SitesToggleText))]
    public partial bool ShowsAllSites { get; set; }

    public string SitesToggleText => ShowsAllSites ? "Fewer sites"
        : _sites.Count - SitesShown == 1 ? "+1 more site" : $"+{_sites.Count - SitesShown} more sites";

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
