using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One kind of backup on Settings › App &amp; data › Backup (ADR 71): export, import, its auto-backup switch and last time, the folder,
/// and the sealed copies on Google Drive to restore from. A restore is refused during a focus session you started.
/// </summary>
public sealed partial class BackupKindCardViewModel : ObservableObject, IDisposable
{
    private readonly BackupService _backups;
    private readonly AutoBackupService _auto;
    private readonly IDriveFiles _drive;
    private readonly SyncIdentityService _identity;
    private readonly SyncSettingsService _sync;
    private readonly SessionController _sessions;
    private readonly Action _restartApp;
    private readonly TimeProvider _time;
    private string? _openedText;
    private BackupContent? _opened;
    private bool _loading;

    public BackupKindCardViewModel(
        BackupKind kind, BackupService backups, AutoBackupService auto, IDriveFiles drive, SyncIdentityService identity, SyncSettingsService sync,
        SessionController sessions, Action restartApp, TimeProvider time)
    {
        Kind = kind;
        _backups = backups;
        _auto = auto;
        _drive = drive;
        _identity = identity;
        _sync = sync;
        _sessions = sessions;
        _restartApp = restartApp;
        _time = time;
        _auto.Changed += OnAutoChanged;
        _sync.Changed += OnSyncChanged;
        Refresh();
    }

    public BackupKind Kind { get; }

    public string Title => BackupKinds.Title(Kind);

    public string Description => Kind switch
    {
        BackupKind.SyncedSettings => "Profiles and rules, the day plan, sleep and bedtime reminders, the day bar, your daily goal and the mode"
                                     + (_sync.Current.ShareTaskLists ? ", and your task lists." : ".") + " The same on every device.",
        BackupKind.DeviceSettings => "Everything set on this PC alone (both modes' copies), and its calendars. Restores on a PC only.",
        _ => "Tasks, Later, come-backs and task categories."
             + (_sync.Current.ShareTaskLists ? " They also travel in the synced settings while they're shared." : " Kept on this PC only, as they aren't shared."),
    };

    public string Extension => BackupKinds.Extension(Kind);

    /// <summary>Auto-backup and import: always, except task lists while they are shared (the synced settings carry them then).</summary>
    public bool ShowsAutoAndImport => Kind != BackupKind.TaskLists || !_sync.Current.ShareTaskLists;

    [ObservableProperty]
    public partial bool AutoOn { get; set; }

    [ObservableProperty]
    public partial string LastText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>A file was opened and waits for Replace or Merge.</summary>
    [ObservableProperty]
    public partial bool IsOpened { get; set; }

    [ObservableProperty]
    public partial bool NeedsPassword { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NeedsRestart { get; set; }

    public ObservableCollection<DriveBackupInfo> DriveBackups { get; } = new();

    [ObservableProperty]
    public partial DriveBackupInfo? SelectedDriveBackup { get; set; }

    public bool HasDriveBackups => DriveBackups.Count > 0;

    /// <summary>Picks where to save (the suggested name); set by the view.</summary>
    public Func<string, Task<string?>>? PickSaveFile { get; set; }

    /// <summary>Picks a file to open; set by the view.</summary>
    public Func<Task<string?>>? PickOpenFile { get; set; }

    public void Refresh()
    {
        _loading = true;
        AutoOn = Kind switch
        {
            BackupKind.SyncedSettings => _auto.Settings.Synced,
            BackupKind.DeviceSettings => _auto.Settings.Device,
            _ => _auto.Settings.Tasks,
        };
        _loading = false;
        LastText = _auto.LastWritten(Kind) is DateTimeOffset at
            ? $"Last auto-backup {TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone).ToString("MMM d, h:mm tt", CultureInfo.CurrentCulture)}"
            : "No auto-backup yet";
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ShowsAutoAndImport));
    }

    partial void OnAutoOnChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        AutoBackupSettings settings = _auto.Settings;
        _ = _auto.SaveSettingsAsync(Kind switch
        {
            BackupKind.SyncedSettings => settings with { Synced = value },
            BackupKind.DeviceSettings => settings with { Device = value },
            _ => settings with { Tasks = value },
        });
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (PickSaveFile is null || await PickSaveFile($"{AppBrand.ProductName} {Title} {stamp}{Extension}") is not string path)
        {
            return;
        }

        try
        {
            await _backups.ExportKindAsync(Kind, path, string.IsNullOrEmpty(Password) ? null : Password);
            Status = $"Saved to {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Not saved: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (PickOpenFile is null || await PickOpenFile() is not string path)
        {
            return;
        }

        try
        {
            _openedText = await File.ReadAllTextAsync(path);
            NeedsPassword = BackupService.Inspect(_openedText) == BackupProtection.Password;
            if (!NeedsPassword)
            {
                Open(_backups.Read(_openedText, null));
            }
            else
            {
                Status = "This backup has a password: type it, then Open.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Status = $"Can't open it: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenWithPassword()
    {
        if (_openedText is null)
        {
            return;
        }

        try
        {
            Open(_backups.Read(_openedText, Password));
            NeedsPassword = false;
        }
        catch (Exception ex) when (ex is BackupPasswordException or FormatException)
        {
            Status = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadDriveBackupsAsync()
    {
        DriveBackups.Clear();
        try
        {
            foreach (DriveBackupInfo backup in await _drive.ListSettingsBackupsAsync(BackupKinds.Name(Kind), CancellationToken.None))
            {
                DriveBackups.Add(backup);
            }

            SelectedDriveBackup = DriveBackups.FirstOrDefault();
            Status = DriveBackups.Count == 0 ? "No backups of this kind on Google Drive yet." : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Status = $"Google Drive didn't answer: {ex.Message}";
        }

        OnPropertyChanged(nameof(HasDriveBackups));
    }

    [RelayCommand]
    private async Task OpenDriveBackupAsync()
    {
        if (SelectedDriveBackup is not DriveBackupInfo backup || _identity.Key is not SyncKey key)
        {
            Status = "Link this PC to your devices first: the Drive backups are sealed with your group's key.";
            return;
        }

        try
        {
            byte[] sealedBytes = await _drive.DownloadAsync(backup.Id, CancellationToken.None);
            if (SealedSettingsBackup.Open(sealedBytes, key) is not string json)
            {
                Status = "This backup was sealed with another key (made before a device was removed, or by another group).";
                return;
            }

            Open(BackupService.ReadContent(json));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException)
        {
            Status = $"Can't open it: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task ReplaceAsync() => RestoreAsync(overwrite: true);

    [RelayCommand]
    private Task MergeAsync() => RestoreAsync(overwrite: false);

    [RelayCommand]
    private void Cancel()
    {
        _opened = null;
        IsOpened = false;
        Status = null;
    }

    [RelayCommand]
    private void Restart() => _restartApp();

    [RelayCommand]
    private Task BackUpNowAsync() => _auto.BackUpNowAsync();

    [RelayCommand]
    private void OpenFolder()
    {
        string folder = _auto.FolderFor(Kind);
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    public void Dispose()
    {
        _auto.Changed -= OnAutoChanged;
        _sync.Changed -= OnSyncChanged;
    }

    private void OnAutoChanged(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    private void OnSyncChanged(object? sender, SyncSettings e) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    private void Open(BackupContent content)
    {
        if (BackupKinds.Parse(content.Kind) is BackupKind kind && kind != Kind)
        {
            Status = $"That's a {BackupKinds.Title(kind).ToLowerInvariant()} backup: import it on its own card.";
            return;
        }

        if (Kind == BackupKind.DeviceSettings && content.Platform is string platform && platform != BackupKinds.PlatformPc)
        {
            Status = "That backup is from a phone: a PC's settings can only come from a PC.";
            return;
        }

        _opened = content;
        IsOpened = true;
        Status = $"Backup from {TimeZoneInfo.ConvertTime(content.CreatedAt, _time.LocalTimeZone).ToString("MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)} "
                 + $"(version {content.AppVersion}). Replace what's here, or merge it in?";
    }

    private async Task RestoreAsync(bool overwrite)
    {
        if (_opened is null)
        {
            return;
        }

        if (_sessions.IsEnforcing)
        {
            Status = "Restoring waits until the focus session you started is over.";
            return;
        }

        RestoreResult result = await _backups.RestoreAsync(_opened, BackupKinds.Parts(Kind, true) & _opened.Parts, overwrite);
        _opened = null;
        IsOpened = false;
        NeedsRestart = result.NeedsRestart;
        Status = string.Join(" ", result.Summary) + (result.NeedsRestart ? " Restart the app to finish." : string.Empty);
    }
}
