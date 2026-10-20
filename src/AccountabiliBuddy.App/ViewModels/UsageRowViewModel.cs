using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One app or site in a History top list, with a bar relative to the list's largest entry, its icon (or a letter
/// badge), and renaming in place.
/// </summary>
public sealed partial class UsageRowViewModel : ObservableObject
{
    private static readonly Color[] BadgeColors =
    {
        Color.Parse("#0F766E"), Color.Parse("#2563EB"), Color.Parse("#9333EA"), Color.Parse("#DB2777"),
        Color.Parse("#C2410C"), Color.Parse("#4D7C0F"), Color.Parse("#475569"), Color.Parse("#B45309"),
    };

    private readonly Func<UsageRowViewModel, string?, Task> _rename;

    /// <param name="realNames">The process names or domains this row adds up (several when they share a display name).</param>
    /// <param name="fraction">0–1, this entry's time divided by the top entry's time.</param>
    /// <param name="rename">Saves a new display name for <paramref name="realNames"/> (null = back to the real name).</param>
    public UsageRowViewModel(
        IReadOnlyList<string> realNames, string name, string timeText, double fraction, Bitmap? icon, Func<UsageRowViewModel, string?, Task> rename)
    {
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

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; }

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
