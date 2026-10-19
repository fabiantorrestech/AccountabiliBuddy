using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Backup;
using AccountabiliBuddy.Core.Backup;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Export and import of backups (Settings tab; import only in the setup assistant). Export: tick parts, then either
/// with secrets (password required) or without. Import: pick a file, enter its password if it has one, tick parts,
/// then Overwrite / Merge / Cancel when there is something here already (Restore otherwise). Importing is locked
/// during a focus session. Restored settings or calendars restart the app so every service loads them.
/// </summary>
public sealed partial class BackupViewModel : ObservableObject, IDisposable
{
    private const int MinPasswordLength = 8;
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    private readonly BackupService _backups;
    private readonly SessionController _sessions;
    private readonly Action _restartApp;
    private string? _fileText;
    private BackupContent? _opened;

    public BackupViewModel(BackupService backups, SessionController sessions, Action restartApp, bool showExport)
    {
        _backups = backups;
        _sessions = sessions;
        _restartApp = restartApp;
        ShowExport = showExport;
        ExportPassword = ExportPasswordConfirm = ImportPassword = string.Empty;
        ExportProfiles = ExportSettings = ExportCalendars = ExportHistory = true;
        IsLocked = sessions.IsEnforcing;
        _sessions.StateChanged += OnSessionStateChanged;
    }

    public bool ShowExport { get; }

    /// <summary>Set by the view: a save-file dialog with a suggested file name; returns the chosen path.</summary>
    public Func<string, Task<string?>>? PickSaveFile { get; set; }

    /// <summary>Set by the view: an open-file dialog; returns the chosen path.</summary>
    public Func<Task<string?>>? PickOpenFile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(ChooseBackupCommand), nameof(OpenWithPasswordCommand),
        nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseBackupCommand), nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool IsLocked { get; set; }

    // ----- Export -----

    [ObservableProperty]
    public partial bool ExportProfiles { get; set; }

    [ObservableProperty]
    public partial bool ExportSettings { get; set; }

    [ObservableProperty]
    public partial bool ExportCalendars { get; set; }

    [ObservableProperty]
    public partial bool ExportHistory { get; set; }

    /// <summary>Include calendar links, Google sign-ins and history details; the file is then password-protected.</summary>
    [ObservableProperty]
    public partial bool IncludeSecrets { get; set; }

    [ObservableProperty]
    public partial string ExportPassword { get; set; }

    [ObservableProperty]
    public partial string ExportPasswordConfirm { get; set; }

    [ObservableProperty]
    public partial string? ExportStatus { get; set; }

    // ----- Import -----

    [ObservableProperty]
    public partial bool NeedsPassword { get; set; }

    [ObservableProperty]
    public partial string ImportPassword { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool IsOpened { get; set; }

    [ObservableProperty]
    public partial string? OpenedSummary { get; set; }

    [ObservableProperty]
    public partial bool HasExistingData { get; set; }

    [ObservableProperty]
    public partial bool HasProfilesPart { get; set; }

    [ObservableProperty]
    public partial bool HasSettingsPart { get; set; }

    [ObservableProperty]
    public partial bool HasCalendarsPart { get; set; }

    [ObservableProperty]
    public partial bool HasHistoryPart { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool RestoreProfiles { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool RestoreSettings { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool RestoreCalendars { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OverwriteCommand), nameof(MergeCommand))]
    public partial bool RestoreHistory { get; set; }

    [ObservableProperty]
    public partial string? ImportStatus { get; set; }

    private bool CanExport => !IsBusy;

    private bool CanChooseBackup => !IsBusy && !IsLocked;

    private bool CanOpenWithPassword => !IsBusy;

    private bool CanRestore => IsOpened && !IsBusy && !IsLocked && SelectedRestoreParts != BackupParts.None;

    private BackupParts SelectedRestoreParts =>
        (RestoreProfiles && HasProfilesPart ? BackupParts.Profiles : BackupParts.None)
        | (RestoreSettings && HasSettingsPart ? BackupParts.Settings : BackupParts.None)
        | (RestoreCalendars && HasCalendarsPart ? BackupParts.Calendars : BackupParts.None)
        | (RestoreHistory && HasHistoryPart ? BackupParts.History : BackupParts.None);

    public void Dispose()
    {
        _sessions.StateChanged -= OnSessionStateChanged;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        BackupParts parts = (ExportProfiles ? BackupParts.Profiles : BackupParts.None)
                            | (ExportSettings ? BackupParts.Settings : BackupParts.None)
                            | (ExportCalendars ? BackupParts.Calendars : BackupParts.None)
                            | (ExportHistory ? BackupParts.History : BackupParts.None);
        if (parts == BackupParts.None)
        {
            ExportStatus = "Tick at least one part to export.";
            return;
        }

        if (IncludeSecrets && ExportPassword.Length < MinPasswordLength)
        {
            ExportStatus = $"Choose a password of at least {MinPasswordLength} characters.";
            return;
        }

        if (IncludeSecrets && ExportPassword != ExportPasswordConfirm)
        {
            ExportStatus = "The two passwords don't match.";
            return;
        }

        string suggested = $"AccountabiliBuddy-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{BackupEnvelope.FileExtension}";
        string? path = PickSaveFile is null ? null : await PickSaveFile(suggested);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _backups.ExportAsync(path, parts, IncludeSecrets ? ExportPassword : null);
            ExportStatus = $"Saved {Path.GetFileName(path)}. "
                           + (IncludeSecrets
                               ? "Keep the password somewhere safe: without it the file can't be opened."
                               : "Calendar links, Google sign-ins and history details were left out.");
            ExportPassword = ExportPasswordConfirm = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExportStatus = $"Couldn't save the backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChooseBackup))]
    private async Task ChooseBackupAsync()
    {
        string? path = PickOpenFile is null ? null : await PickOpenFile();
        if (path is null)
        {
            return;
        }

        CancelImport();
        try
        {
            _fileText = await File.ReadAllTextAsync(path);
            if (BackupService.Inspect(_fileText) == BackupProtection.Password)
            {
                NeedsPassword = true;
                ImportStatus = $"{Path.GetFileName(path)} is password-protected. Enter its password.";
                return;
            }

            await OpenAsync(null);
        }
        catch (FormatException ex)
        {
            ImportStatus = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ImportStatus = $"Couldn't read the file: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenWithPassword))]
    private async Task OpenWithPasswordAsync()
    {
        await OpenAsync(ImportPassword);
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task OverwriteAsync()
    {
        return RestoreAsync(overwrite: true);
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task MergeAsync()
    {
        return RestoreAsync(overwrite: false);
    }

    [RelayCommand]
    private void CancelImport()
    {
        _fileText = null;
        _opened = null;
        IsOpened = NeedsPassword = HasExistingData = false;
        HasProfilesPart = HasSettingsPart = HasCalendarsPart = HasHistoryPart = false;
        ImportPassword = string.Empty;
        OpenedSummary = ImportStatus = null;
    }

    private async Task OpenAsync(string? password)
    {
        if (_fileText is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            BackupContent content = await Task.Run(() => _backups.Read(_fileText, password));
            _opened = content;
            HasProfilesPart = RestoreProfiles = content.Profiles is not null;
            HasSettingsPart = RestoreSettings = content.Settings is not null;
            HasCalendarsPart = RestoreCalendars = content.CalendarSources is not null;
            HasHistoryPart = RestoreHistory = content.History is not null;
            HasExistingData = await _backups.HasExistingDataAsync(content.Parts);
            NeedsPassword = false;
            ImportPassword = string.Empty;
            IsOpened = true;
            OpenedSummary = $"Backup from {content.CreatedAt.ToLocalTime():d MMM yyyy HH:mm} (version {content.AppVersion}), "
                            + (content.IncludesSecrets ? "with secrets." : "without secrets.");
            ImportStatus = HasExistingData
                ? "This app already has settings. Overwrite replaces the ticked parts (a backup of the current state is saved first); Merge adds what's new and keeps your current settings."
                : null;
        }
        catch (BackupPasswordException ex)
        {
            NeedsPassword = true;
            ImportStatus = ex.Message;
        }
        catch (FormatException ex)
        {
            ImportStatus = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync(bool overwrite)
    {
        if (_opened is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            RestoreResult result = await _backups.RestoreAsync(_opened, SelectedRestoreParts, overwrite);
            ImportStatus = string.Join(" ", result.Summary)
                           + (result.SafetyBackupPath is null ? string.Empty : $" The previous state was saved to {result.SafetyBackupPath}.")
                           + (result.NeedsRestart ? " Restarting to load everything…" : string.Empty);
            IsOpened = false;
            if (result.NeedsRestart)
            {
                await Task.Delay(RestartDelay);
                _restartApp();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            ImportStatus = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        IsLocked = _sessions.IsEnforcing;
    }
}
