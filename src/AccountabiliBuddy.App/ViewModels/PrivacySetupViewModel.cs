using AccountabiliBuddy.App.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The data permissions in the Setup tab: screen reading (OCR, off until granted) and activity history
/// (on by default), plus "Delete my data", which needs a second click within a few seconds to confirm.
/// </summary>
public sealed partial class PrivacySetupViewModel : ObservableObject
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(5);
    private const string DeleteLabel = "Delete my data…";
    private const string ConfirmLabel = "Click again to delete everything";

    private readonly PrivacySettingsService _privacy;
    private readonly ActivityRecorder _recorder;
    private readonly bool _ocrAvailable;
    private readonly DispatcherTimer _confirmTimeout;

    // Number of stored log entries; -1 when it could not be read.
    private long _entryCount;

    public PrivacySetupViewModel(PrivacySettingsService privacy, ActivityRecorder recorder, bool ocrAvailable)
    {
        _privacy = privacy;
        _recorder = recorder;
        _ocrAvailable = ocrAvailable;
        ScreenReadingStatus = ActivityLogStatus = string.Empty;
        DeleteButtonText = DeleteLabel;

        _confirmTimeout = new DispatcherTimer { Interval = ConfirmWindow };
        _confirmTimeout.Tick += (_, _) => ResetDeleteConfirmation();

        _privacy.Changed += (_, _) => Refresh();
        _recorder.LogChanged += async (_, _) => await RefreshEntryCountAsync();
        Refresh();
        _ = RefreshEntryCountAsync();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantScreenReadingCommand), nameof(RevokeScreenReadingCommand))]
    public partial bool IsScreenReadingGranted { get; set; }

    [ObservableProperty]
    public partial string ScreenReadingStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantActivityLogCommand), nameof(RevokeActivityLogCommand))]
    public partial bool IsActivityLogGranted { get; set; }

    [ObservableProperty]
    public partial string ActivityLogStatus { get; set; }

    [ObservableProperty]
    public partial string DeleteButtonText { get; set; }

    [ObservableProperty]
    public partial string? DeleteStatus { get; set; }

    private bool CanGrantScreenReading => !IsScreenReadingGranted && _ocrAvailable;

    private bool CanRevokeScreenReading => IsScreenReadingGranted;

    private bool CanGrantActivityLog => !IsActivityLogGranted;

    private bool CanRevokeActivityLog => IsActivityLogGranted;

    [RelayCommand(CanExecute = nameof(CanGrantScreenReading))]
    private Task GrantScreenReadingAsync()
    {
        return _privacy.SetScreenReadingAsync(true);
    }

    [RelayCommand(CanExecute = nameof(CanRevokeScreenReading))]
    private Task RevokeScreenReadingAsync()
    {
        return _privacy.SetScreenReadingAsync(false);
    }

    [RelayCommand(CanExecute = nameof(CanGrantActivityLog))]
    private Task GrantActivityLogAsync()
    {
        return _privacy.SetActivityLogAsync(true);
    }

    [RelayCommand(CanExecute = nameof(CanRevokeActivityLog))]
    private Task RevokeActivityLogAsync()
    {
        return _privacy.SetActivityLogAsync(false);
    }

    [RelayCommand]
    private async Task DeleteDataAsync()
    {
        if (DeleteButtonText != ConfirmLabel)
        {
            DeleteButtonText = ConfirmLabel;
            DeleteStatus = null;
            _confirmTimeout.Start();
            return;
        }

        ResetDeleteConfirmation();
        try
        {
            await _recorder.DeleteAllAsync();
            DeleteStatus = "All activity history was deleted.";
        }
        catch (SqliteException ex)
        {
            DeleteStatus = $"Could not delete the history: {ex.Message}";
        }

        await RefreshEntryCountAsync();
    }

    private void ResetDeleteConfirmation()
    {
        _confirmTimeout.Stop();
        DeleteButtonText = DeleteLabel;
    }

    private void Refresh()
    {
        PrivacySettings settings = _privacy.Current;
        IsScreenReadingGranted = settings.ScreenReadingEnabled;
        IsActivityLogGranted = settings.ActivityLogEnabled;

        ScreenReadingStatus = !_ocrAvailable
            ? "Windows has no text-recognition language installed for your display language, so this can't be turned on."
            : settings.ScreenReadingEnabled
                ? "On. Used only during focus, only for windows no other rule recognises, and only when the profile has screen-text rules."
                : "Off. Turn it on to use screen-text rules (Profiles → match on OcrText).";

        UpdateActivityLogStatus();
    }

    private async Task RefreshEntryCountAsync()
    {
        try
        {
            _entryCount = await _recorder.CountEntriesAsync();
        }
        catch (SqliteException)
        {
            _entryCount = -1;
        }

        UpdateActivityLogStatus();
    }

    private void UpdateActivityLogStatus()
    {
        string count = _entryCount < 0 ? "unknown number of" : _entryCount.ToString("N0");
        ActivityLogStatus = (_privacy.Current.ActivityLogEnabled ? "On" : "Off (nothing new is recorded)")
            + $" · {count} entries stored · kept {ActivityRecorder.Retention.TotalDays:0} days. "
            + "Window titles, addresses, screen text and reasons are encrypted for your Windows account.";
    }
}
