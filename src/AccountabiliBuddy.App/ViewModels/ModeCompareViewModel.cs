using System.Collections.ObjectModel;
using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The differences between the modes (ADR 69), under the App mode switch in Settings › App &amp; data: the settings each mode keeps for
/// itself, side by side, only where they differ. "Sync AccountabiliBuddy → ProductaBuddy" or the reverse makes every row agree at once;
/// otherwise pick left or right per row and Apply. Live: a mode switch or a value changed elsewhere shows at once, and picks on rows still
/// there stay. Use from the UI thread only.
/// </summary>
public sealed partial class ModeCompareViewModel : ObservableObject, IDisposable
{
    private readonly ModeSettings _modes;
    private readonly GuidanceModeService _guidance;

    public ModeCompareViewModel(ModeSettings modes, GuidanceModeService guidance)
    {
        _modes = modes;
        _guidance = guidance;
        _modes.Reload += OnChangedElsewhere;
        _guidance.Changed += OnChangedElsewhere;
    }

    public void Dispose()
    {
        _modes.Reload -= OnChangedElsewhere;
        _guidance.Changed -= OnChangedElsewhere;
    }

    private void OnChangedElsewhere(object? sender, EventArgs e) => _ = LoadAsync();

    public static string GuardingName => AppBrand.ProductName;

    public static string CompanionName => AppBrand.CompanionName;

    public static string SyncToCompanionText => $"Sync {AppBrand.ProductName} → {AppBrand.CompanionName}";

    public static string SyncToGuardingText => $"Sync {AppBrand.CompanionName} → {AppBrand.ProductName}";

    public ObservableCollection<ModeCompareRowViewModel> Rows { get; } = new();

    public bool HasRows => Rows.Count > 0;

    /// <summary>At least one row has a side picked, so "Apply picks" does something.</summary>
    public bool HasPicks => Rows.Any(row => row.IsPicked);

    public async Task LoadAsync()
    {
        // Picks carry over to the same setting when it still differs; a row that no longer differs is gone with its pick.
        Dictionary<string, (bool Guarding, bool Companion)> picks = Rows.ToDictionary(
            row => row.Row.Setting.DocumentKey + "/" + row.Row.Setting.Path, row => (row.UseGuarding, row.UseCompanion));
        IReadOnlyList<ModeSettingRow> fresh = await _modes.CompareAsync();
        foreach (ModeCompareRowViewModel old in Rows)
        {
            old.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        foreach (ModeSettingRow row in fresh)
        {
            var item = new ModeCompareRowViewModel(row);
            if (picks.TryGetValue(row.Setting.DocumentKey + "/" + row.Setting.Path, out (bool Guarding, bool Companion) pick))
            {
                item.UseGuarding = pick.Guarding;
                item.UseCompanion = pick.Companion;
            }

            item.PropertyChanged += OnRowChanged;
            Rows.Add(item);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasPicks));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(HasPicks));

    [RelayCommand]
    private Task SyncToCompanionAsync() => ApplyPicksAsync(Rows.Select(row => (row.Row, true)).ToList());

    [RelayCommand]
    private Task SyncToGuardingAsync() => ApplyPicksAsync(Rows.Select(row => (row.Row, false)).ToList());

    [RelayCommand]
    private Task ApplyAsync() => ApplyPicksAsync(Rows.Where(row => row.IsPicked).Select(row => (row.Row, row.UseGuarding)).ToList());

    // Applying raises ModeSettings.Reload, which reloads the rows: the settings now agree, so their rows are gone.
    private async Task ApplyPicksAsync(IReadOnlyList<(ModeSettingRow Row, bool UseGuarding)> picks)
    {
        if (picks.Count > 0)
        {
            await _modes.ApplyAsync(picks);
            await LoadAsync();
        }
    }
}
