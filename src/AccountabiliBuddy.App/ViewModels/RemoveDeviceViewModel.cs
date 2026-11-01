using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "Remove this device" dialog (ADR 54, 55), in pages: what removal does, keep or erase its data, a backup first,
/// a warning about devices on an older version, and the confirmation. Then it removes the device
/// (<see cref="SyncRemovalService.RemoveAsync"/>), makes the backup with the NEW key, and only then applies the keep or erase
/// choice, so nothing is erased before a backup was made or given up. If the backup fails after the key switched, the data
/// stays as it is and the dialog offers to try again, to save a file instead, or to go on without one. Closing the dialog
/// at that point leaves the data kept. Use from the UI thread only.
/// </summary>
public sealed partial class RemoveDeviceViewModel : ObservableObject
{
    private readonly SyncRemovalService _removal;
    private readonly DeviceBackupService _backups;
    private readonly TimeProvider _time;
    private readonly Guid _deviceId;
    private readonly ReceivedDevicesService _received;
    private SyncFile? _snapshot;
    private readonly DateOnly _day;
    private readonly IReadOnlyList<NotWrappedDevice> _notWrapped;
    private RemovalResult? _result;

    public RemoveDeviceViewModel(
        SyncRemovalService removal, ReceivedDevicesService received, DeviceBackupService backups, TimeProvider time, Guid deviceId)
    {
        _removal = removal;
        _received = received;
        _backups = backups;
        _time = time;
        _deviceId = deviceId;
        _day = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        ReceivedDevice? device = received.Devices.FirstOrDefault(candidate => candidate.File.Device.DeviceId == deviceId);
        _snapshot = device?.File;
        Name = device?.File.Device.Name ?? "this device";
        IsPhone = device?.File.Device.Kind == SyncDeviceCard.KindPhone;
        _notWrapped = removal.NotWrappedFor([deviceId]);
        foreach (NotWrappedDevice notWrapped in _notWrapped)
        {
            NotWrappedNames.Add(notWrapped.Name);
        }

        DriveAvailable = backups.DriveAvailable;
        BackupChoice = DriveAvailable ? RemoveBackupChoice.Drive : RemoveBackupChoice.File;
        BackupPath = Path.Combine(DeviceBackupService.DefaultFolder, DeviceBackupService.LocalFileNameFor(Name, deviceId, _day));
        DoneTitle = string.Empty;
        ProblemText = string.Empty;
    }

    /// <summary>Picks where to save the backup file: the folder to start in and the suggested file name; the chosen path or null. Set by the dialog.</summary>
    public Func<string, string, Task<string?>>? PickBackupFile { get; set; }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    public string Name { get; }

    /// <summary>The device is a phone: its watch goes with it.</summary>
    public bool IsPhone { get; }

    /// <summary>Why it can't be removed right now (a focus session you started); null when it can.</summary>
    public string? BlockedReason => _removal.BlockedReason;

    public bool IsBlocked => BlockedReason is not null;

    /// <summary>Google Drive is a sync method in use and signed in: a backup can go there.</summary>
    public bool DriveAvailable { get; }

    /// <summary>The names of the devices that would be cut off too, for the older-version page.</summary>
    public ObservableCollection<string> NotWrappedNames { get; } = [];

    public bool HasNotWrapped => _notWrapped.Count > 0;

    /// <summary>What was done, line by line, on the last page.</summary>
    public ObservableCollection<string> Summary { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWhat), nameof(IsData), nameof(IsBackup), nameof(IsOlder), nameof(IsConfirm), nameof(IsWorking), nameof(IsBackupProblem), nameof(IsDone),
        nameof(IsQuestionPage), nameof(CanGoBack), nameof(NextText), nameof(StepText), nameof(BlockedReason), nameof(IsBlocked))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial RemoveStep Step { get; set; }

    public bool IsWhat => Step == RemoveStep.What;

    public bool IsData => Step == RemoveStep.Data;

    public bool IsBackup => Step == RemoveStep.Backup;

    public bool IsOlder => Step == RemoveStep.Older;

    public bool IsConfirm => Step == RemoveStep.Confirm;

    public bool IsWorking => Step == RemoveStep.Working;

    public bool IsBackupProblem => Step == RemoveStep.BackupProblem;

    public bool IsDone => Step == RemoveStep.Done;

    /// <summary>One of the five question pages (Back, Next and Cancel show).</summary>
    public bool IsQuestionPage => Step is RemoveStep.What or RemoveStep.Data or RemoveStep.Backup or RemoveStep.Older or RemoveStep.Confirm;

    public bool CanGoBack => IsQuestionPage && Step != RemoveStep.What;

    public string NextText => Step == RemoveStep.Confirm ? $"Remove {Name}" : "Next";

    /// <summary>E.g. "Step 2 of 4".</summary>
    public string StepText
    {
        get
        {
            List<RemoveStep> steps = QuestionSteps();
            int index = steps.IndexOf(Step);
            return index < 0 ? string.Empty : $"Step {index + 1} of {steps.Count}";
        }
    }

    /// <summary>The data is erased from this PC (and, as the others apply the removal, from every device); false keeps it, shown as "[name] (removed)".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeepData))]
    public partial bool IsEraseData { get; set; }

    public bool IsKeepData
    {
        get => !IsEraseData;
        set
        {
            if (value)
            {
                IsEraseData = false;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackupDrive), nameof(IsBackupFile), nameof(IsBackupNone))]
    public partial RemoveBackupChoice BackupChoice { get; set; }

    public bool IsBackupDrive
    {
        get => BackupChoice == RemoveBackupChoice.Drive;
        set
        {
            if (value)
            {
                BackupChoice = RemoveBackupChoice.Drive;
            }
        }
    }

    public bool IsBackupFile
    {
        get => BackupChoice == RemoveBackupChoice.File;
        set
        {
            if (value)
            {
                BackupChoice = RemoveBackupChoice.File;
            }
        }
    }

    public bool IsBackupNone
    {
        get => BackupChoice == RemoveBackupChoice.None;
        set
        {
            if (value)
            {
                BackupChoice = RemoveBackupChoice.None;
            }
        }
    }

    /// <summary>The file the backup goes to when the choice is a file; by default in <c>Documents\AccountabiliBuddy backups</c>.</summary>
    [ObservableProperty]
    public partial string BackupPath { get; set; }

    /// <summary>The title of the last page.</summary>
    [ObservableProperty]
    public partial string DoneTitle { get; set; }

    /// <summary>The last page is a failure (nothing was removed).</summary>
    [ObservableProperty]
    public partial bool DoneFailed { get; set; }

    /// <summary>What went wrong with the backup, on the backup-problem page.</summary>
    [ObservableProperty]
    public partial string ProblemText { get; set; }

    /// <summary>Drive refused the backup: saving a file instead is offered.</summary>
    [ObservableProperty]
    public partial bool CanSaveFileInstead { get; set; }

    private List<RemoveStep> QuestionSteps()
    {
        var steps = new List<RemoveStep> { RemoveStep.What, RemoveStep.Data, RemoveStep.Backup };
        if (HasNotWrapped)
        {
            steps.Add(RemoveStep.Older);
        }

        steps.Add(RemoveStep.Confirm);
        return steps;
    }

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        if (PickBackupFile is null)
        {
            return;
        }

        string? path = await PickBackupFile(Path.GetDirectoryName(BackupPath) ?? DeviceBackupService.DefaultFolder, Path.GetFileName(BackupPath));
        if (path is not null)
        {
            BackupPath = path;
            IsBackupFile = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        List<RemoveStep> steps = QuestionSteps();
        int index = steps.IndexOf(Step);
        if (index > 0)
        {
            Step = steps[index - 1];
        }
    }

    [RelayCommand(CanExecute = nameof(IsQuestionPage))]
    private async Task NextAsync()
    {
        if (Step != RemoveStep.Confirm)
        {
            List<RemoveStep> steps = QuestionSteps();
            Step = steps[steps.IndexOf(Step) + 1];
            return;
        }

        if (IsBlocked || _snapshot is null)
        {
            return;
        }

        await RemoveAsync();
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Remove first (the key changes), then the backup with the new key, then the keep or erase choice.
    private async Task RemoveAsync()
    {
        Step = RemoveStep.Working;
        // The copy to back up is taken now, so a file that arrived while the dialog sat open is in it.
        _snapshot = _received.Devices.FirstOrDefault(candidate => candidate.File.Device.DeviceId == _deviceId)?.File ?? _snapshot;
        RemovalResult result = await _removal.RemoveAsync([_deviceId], IsEraseData);
        if (!result.Done)
        {
            DoneFailed = true;
            DoneTitle = result.Conflict ? "Another device changed your devices at the same time" : $"{Name} was not removed";
            Summary.Clear();
            Summary.Add(result.Problem ?? "Something went wrong.");
            Summary.Add(result.Conflict
                ? $"{Name} was not removed by this attempt. Look at your devices and try again if it is still there."
                : "Nothing was changed on this PC.");
            Step = RemoveStep.Done;
            return;
        }

        _result = result;
        await MakeBackupAsync();
    }

    private async Task MakeBackupAsync()
    {
        if (BackupChoice == RemoveBackupChoice.None || _snapshot is null)
        {
            await FinishAsync(null);
            return;
        }

        Step = RemoveStep.Working;
        BackupOutcome outcome = BackupChoice == RemoveBackupChoice.Drive
            ? await _backups.SaveToDriveAsync(_snapshot, _day)
            : await _backups.SaveToFileAsync(_snapshot, BackupPath);
        await HandleBackupAsync(outcome);
    }

    private async Task HandleBackupAsync(BackupOutcome outcome)
    {
        if (outcome.Done)
        {
            await FinishAsync(outcome.Where);
            return;
        }

        ProblemText = outcome.Problem ?? "The backup couldn't be saved.";
        CanSaveFileInstead = BackupChoice == RemoveBackupChoice.Drive;
        Step = RemoveStep.BackupProblem;
    }

    [RelayCommand]
    private Task TryBackupAgainAsync()
    {
        return MakeBackupAsync();
    }

    [RelayCommand]
    private async Task SaveFileInsteadAsync()
    {
        if (_snapshot is null)
        {
            return;
        }

        string path = BackupPath;
        if (PickBackupFile is not null)
        {
            string? picked = await PickBackupFile(Path.GetDirectoryName(BackupPath) ?? DeviceBackupService.DefaultFolder, Path.GetFileName(BackupPath));
            if (picked is null)
            {
                return;
            }

            path = picked;
            BackupPath = picked;
        }

        BackupChoice = RemoveBackupChoice.File;
        Step = RemoveStep.Working;
        await HandleBackupAsync(await _backups.SaveToFileAsync(_snapshot, path));
    }

    [RelayCommand]
    private Task ContinueWithoutBackupAsync()
    {
        return FinishAsync(null);
    }

    // The backup is made or given up: now the keep or erase choice is applied, and the page says what happened.
    private async Task FinishAsync(string? backupWhere)
    {
        // Your other devices erase (or keep) as they apply the change; this PC does it now, after the backup.
        string? reason;
        try
        {
            reason = await _removal.ApplyDataChoiceAsync(_deviceId, IsEraseData ? RemovedDevice.EraseData : RemovedDevice.KeepData);
        }
        catch (SqliteException)
        {
            reason = "this PC's storage was busy; its copy is still here.";
        }

        DoneFailed = false;
        DoneTitle = $"{Name} was removed";
        Summary.Clear();
        Summary.Add($"{Name} can no longer read new data from your devices. To use it again it has to be linked again.");
        if (IsPhone)
        {
            Summary.Add("Its watch goes with it.");
        }

        if (reason is not null)
        {
            Summary.Add(IsEraseData
                ? $"Its data was not erased on this PC: {reason}"
                : $"The choice to keep its data could not be saved: {reason}");
        }
        else
        {
            Summary.Add(IsEraseData
                ? "Its data was erased from this PC. Each of your other devices erases it when it applies the change (a PC in a focus session waits until it ends). Shared lists keep the items it added."
                : $"Its data is kept on every device. History and Stats show it as \"{Name} (removed)\".");
        }

        Summary.Add(backupWhere is null
            ? (BackupChoice == RemoveBackupChoice.None ? "No backup was made." : "It was removed without a backup.")
            : $"Backup saved: {backupWhere}");

        if (_result is { NotWrapped.Count: > 0 } result)
        {
            Summary.Add($"{result.NotWrapped.Count} other device(s) could not get the new key, so they are cut off too: update them, then link them again.");
        }

        if (_result is { FailedMethods: > 0 } methods)
        {
            Summary.Add($"{methods.FailedMethods} sync method(s) did not take the change: a device that syncs only through those will not hear of it until you link it again.");
        }

        Step = RemoveStep.Done;
    }
}
