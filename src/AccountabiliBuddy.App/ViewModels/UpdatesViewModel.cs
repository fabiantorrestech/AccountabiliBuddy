using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Settings → Updates: the current version, Check now, Install and restart, and automatic updates.</summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private bool _loading;

    public UpdatesViewModel(UpdateService updates)
    {
        _updates = updates;
        _updates.Changed += (_, _) => Refresh();
        Status = string.Empty;
        Refresh();
    }

    public string VersionText => $"Installed version: {Services.Backup.BackupService.AppVersion}";

    public bool CanUpdate => _updates.CanUpdate;

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial bool HasUpdate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(InstallCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool AutoInstall { get; set; }

    public string InstallText => _updates.AvailableVersion is string version ? $"Install {version} and restart" : "Install and restart";

    private bool CanCheck => CanUpdate && !IsBusy;

    private bool CanInstall => CanUpdate && HasUpdate && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private Task CheckAsync()
    {
        return _updates.CheckAsync(manual: true);
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync()
    {
        return _updates.InstallAsync(automatic: false);
    }

    partial void OnAutoInstallChanged(bool value)
    {
        if (!_loading && value != _updates.Settings.AutoInstall)
        {
            _ = _updates.SetAutoInstallAsync(value);
        }
    }

    private void Refresh()
    {
        _loading = true;
        Status = _updates.Status;
        HasUpdate = _updates.AvailableVersion is not null;
        IsBusy = _updates.IsBusy;
        AutoInstall = _updates.Settings.AutoInstall;
        OnPropertyChanged(nameof(InstallText));
        _loading = false;
    }
}
