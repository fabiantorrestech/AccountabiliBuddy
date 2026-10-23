using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → Focus sessions → Hyperfocus reminder: on or off, after how many minutes of unbroken focus, and which profiles
/// get it. A reminder, not a block, so changes apply at once on Save.
/// </summary>
public sealed partial class HyperfocusViewModel : ObservableObject
{
    private readonly HyperfocusGuard _guard;
    private readonly ConfigurationService _configuration;

    public HyperfocusViewModel(HyperfocusGuard guard, ConfigurationService configuration)
    {
        _guard = guard;
        _configuration = configuration;
        IsOn = guard.Settings.IntervalMinutes > 0;
        IntervalMinutes = guard.Settings.IntervalMinutes > 0 ? guard.Settings.IntervalMinutes : HyperfocusSettings.Default.IntervalMinutes;
        LoadProfiles();
        _configuration.Changed += (_, _) => LoadProfiles();
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial decimal? IntervalMinutes { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public ObservableCollection<HyperfocusProfileRowViewModel> Profiles { get; } = new();

    [RelayCommand]
    private async Task SaveAsync()
    {
        int minutes = (int)Math.Clamp(IntervalMinutes ?? HyperfocusSettings.Default.IntervalMinutes, 15, 240);
        IReadOnlyList<Guid> off = Profiles.Where(row => !row.Reminds).Select(row => row.Id).ToList();
        await _guard.SaveAsync(new HyperfocusSettings(IsOn ? minutes : 0, off));
        Message = "Saved.";
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (Core.Domain.FocusProfile profile in _configuration.Current.Profiles)
        {
            Profiles.Add(new HyperfocusProfileRowViewModel(profile.Id, profile.Name, !_guard.Settings.OffForProfiles.Contains(profile.Id)));
        }
    }
}
