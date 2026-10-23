using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → Your day → "Work hours on this computer" (ADR 27): the switch, the apps that wait, and "Stop for today" with
/// the end-early wait and reason. Changes apply at once (not with Settings' Save): turning it on is stricter; off, removing
/// an app or stopping waits like ending a session early.
/// </summary>
public sealed partial class WorkHoursViewModel : ObservableObject
{
    // A stop asked for and not confirmed within the wait plus this is dropped (ask again), so the wait can't be done ahead.
    private static readonly TimeSpan ConfirmWithin = TimeSpan.FromMinutes(2);

    private readonly WorkHoursGuard _guard;
    private readonly SessionController _sessions;
    private DateTimeOffset? _stopAskedAt;

    public WorkHoursViewModel(WorkHoursGuard guard, SessionController sessions)
    {
        _guard = guard;
        _sessions = sessions;
        AppsText = string.Join(", ", guard.Settings.Apps);
        StopReason = string.Empty;
        _guard.Changed += (_, _) => Refresh();
        _sessions.Ticked += (_, _) => RefreshStopWait();
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>E.g. "Guarding: work until 5:00 PM" or "On: guards during your plan's work parts".</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>The apps that wait, comma separated (process names).</summary>
    [ObservableProperty]
    public partial string AppsText { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsGuarding { get; set; }

    /// <summary>Work still to come today: "Stop for today…" shows (and turning off waits for it).</summary>
    [ObservableProperty]
    public partial bool HasWorkLeftToday { get; set; }

    /// <summary>"Stop for today" asked: the wait runs and the reason box shows.</summary>
    [ObservableProperty]
    public partial bool IsStopping { get; set; }

    [ObservableProperty]
    public partial string StopReason { get; set; }

    /// <summary>E.g. "Wait 0:42" or "You can stop now".</summary>
    [ObservableProperty]
    public partial string StopWaitText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task TurnOnAsync()
    {
        await _guard.TurnOnAsync();
        Message = null;
    }

    [RelayCommand]
    private async Task TurnOffAsync()
    {
        Message = await _guard.TurnOffAsync() ? null : "Work is left today: stop for today first (it waits like ending a session early).";
    }

    [RelayCommand]
    private async Task SaveAppsAsync()
    {
        bool saved = await _guard.SetAppsAsync(AppsText.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries));
        Message = saved ? "Saved." : "Removing an app waits until today's work is over (or stop for today first).";
        AppsText = string.Join(", ", _guard.Settings.Apps);
    }

    [RelayCommand]
    private void BeginStop()
    {
        _stopAskedAt = _sessions.Now;
        IsStopping = true;
        RefreshStopWait();
    }

    [RelayCommand]
    private async Task ConfirmStopAsync()
    {
        if (_stopAskedAt is not DateTimeOffset asked)
        {
            return;
        }

        if (_sessions.Now - asked > _guard.StopWait + ConfirmWithin)
        {
            CancelStop();
            Message = "That was asked too long ago: ask again (the wait starts now).";
            return;
        }

        if (await _guard.StopForTodayAsync(StopReason, asked))
        {
            IsStopping = false;
            _stopAskedAt = null;
            StopReason = string.Empty;
            Message = "Stopped until tomorrow.";
        }
        else
        {
            Message = _guard.WaitLeft(asked) > TimeSpan.Zero ? "The wait isn't over yet." : "Write a longer reason.";
        }
    }

    [RelayCommand]
    private void CancelStop()
    {
        IsStopping = false;
        _stopAskedAt = null;
    }

    private void Refresh()
    {
        IsOn = _guard.Settings.On;
        IsGuarding = _guard.IsGuarding;
        HasWorkLeftToday = _guard.HasWorkLeftToday;
        StatusText = !IsOn ? "Off"
            : IsGuarding ? $"Guarding: {_guard.UntilText}"
            : "On: guards during your plan's work parts (not now)";
    }

    private void RefreshStopWait()
    {
        HasWorkLeftToday = _guard.HasWorkLeftToday;
        if (_stopAskedAt is DateTimeOffset asked)
        {
            TimeSpan left = _guard.WaitLeft(asked);
            StopWaitText = left > TimeSpan.Zero ? $"Wait {(int)left.TotalMinutes}:{left.Seconds:00}" : "You can stop now";
        }
    }
}
