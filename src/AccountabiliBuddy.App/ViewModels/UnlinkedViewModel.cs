using AccountabiliBuddy.App.Services;
using System.Globalization;
using AccountabiliBuddy.App.Services.Sync;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// This PC was removed from the user's synced devices, or cut off by a removal (ADR 54): it syncs nothing until the user
/// picks one of three things, none of them a default. "Add this PC again" asks for a link code from a device that is still
/// linked and joins with this PC's device id; "Continue unsynced here" leaves the group and keeps all data, including what
/// was received; "Start from scratch" opens the start-over dialog. The same three choices are in a dialog at start and on the
/// Devices page. Joining and continuing are refused during a focus session the user started. Use from the UI thread only.
/// </summary>
public sealed partial class UnlinkedViewModel : ObservableObject, IDisposable
{
    private readonly SyncRemovalService _removal;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private readonly Action _openStartOver;

    /// <param name="openStartOver">Opens the start-over dialog (typed confirmation); set by the app.</param>
    public UnlinkedViewModel(SyncRemovalService removal, SessionController sessions, TimeProvider time, Action openStartOver)
    {
        _removal = removal;
        _sessions = sessions;
        _time = time;
        _openStartOver = openStartOver;
        JoinCode = string.Empty;
        Title = string.Empty;
        ByLine = string.Empty;
        removal.Changed += OnRemovalChanged;
        Refresh();
    }

    /// <summary>Raised when this PC is no longer unlinked (it joined a group again, or continued unsynced).</summary>
    public event EventHandler? Resolved;

    /// <summary>True while this PC is unlinked: the choices show.</summary>
    [ObservableProperty]
    public partial bool IsUnlinked { get; set; }

    /// <summary>"This PC was removed from your synced devices".</summary>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>"by Work Laptop on Oct 28, 3:20 PM".</summary>
    [ObservableProperty]
    public partial string ByLine { get; set; }

    /// <summary>The "add this PC again" box is open: a link code goes in it.</summary>
    [ObservableProperty]
    public partial bool IsAddingAgain { get; set; }

    [ObservableProperty]
    public partial string JoinCode { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public void Dispose()
    {
        _removal.Changed -= OnRemovalChanged;
    }

    private void OnRemovalChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        bool was = IsUnlinked;
        UnlinkedState? state = _removal.Unlinked;
        IsUnlinked = state is not null;
        if (state is not null)
        {
            Title = state.Removed ? "This PC was removed from your synced devices" : "This PC was cut off from your synced devices";
            DateTimeOffset local = TimeZoneInfo.ConvertTime(state.At, _time.LocalTimeZone);
            ByLine = $"by {state.RemovedByName} on {local.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture)}"
                + (state.Removed ? string.Empty : ". It couldn't get the new key, most likely because this app is older than the removing device's");
        }
        else if (was)
        {
            Resolved?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void AddAgain()
    {
        IsAddingAgain = true;
        Message = "On a device that is still linked, open Devices → Link a device (on a PC: Settings › Devices & sync) and type its link code here.";
    }

    [RelayCommand]
    private async Task JoinAgainAsync()
    {
        if (_sessions.IsEnforcing)
        {
            Message = StartOverLocked;
            return;
        }

        if (await _removal.RejoinAsync(JoinCode.Trim()))
        {
            JoinCode = string.Empty;
            IsAddingAgain = false;
            Message = null;
        }
        else
        {
            Message = "That isn't a link code (40 characters, from Devices on your other device).";
        }
    }

    [RelayCommand]
    private async Task ContinueUnsyncedAsync()
    {
        Message = await _removal.ContinueUnsyncedAsync();
    }

    [RelayCommand]
    private void StartFromScratch()
    {
        _openStartOver();
    }

    private const string StartOverLocked = "Locked while a focus session you started is running.";
}
