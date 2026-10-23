using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Pre-Work window (ADR 31): "Is today a work day?", then either nothing more (a work day) or the ten-minute day-off
/// wait with a box for the day's plans. An emergency unlock folds out: a long reason and a code to retype, two a week.
/// </summary>
public sealed partial class PreWorkPromptViewModel : ObservableObject
{
    private readonly PreWorkService _service;
    private DateTimeOffset? _dayOffAskedAt;

    public PreWorkPromptViewModel(DateTime partEnd, DateTimeOffset? dayOffAskedAt, PreWorkService service)
    {
        _service = service;
        UntilText = $"Pre-work until {partEnd:t}";
        Plans = string.Empty;
        WaitText = string.Empty;
        Reason = string.Empty;
        TypedCode = string.Empty;
        UnlockCode = PreWorkService.NewUnlockCode();
        UnlocksText = string.Empty;
        if (dayOffAskedAt is DateTimeOffset asked)
        {
            StartDayOffWait(asked);
        }
    }

    /// <summary>E.g. "Pre-work until 8:30 AM".</summary>
    public string UntilText { get; }

    /// <summary>True once "No, day off" was chosen: the wait shows instead of the question.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsking))]
    public partial bool IsWaiting { get; set; }

    public bool IsAsking => !IsWaiting;

    /// <summary>What the user wants to do with the day, one per line; kept as come-backs.</summary>
    [ObservableProperty]
    public partial string Plans { get; set; }

    /// <summary>"Day off in 7:42", then empty.</summary>
    [ObservableProperty]
    public partial string WaitText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TakeDayOffCommand))]
    public partial bool CanTakeDayOff { get; set; }

    [ObservableProperty]
    public partial bool ShowsUnlock { get; set; }

    [ObservableProperty]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial string TypedCode { get; set; }

    /// <summary>The code to retype; a new one each time the window opens.</summary>
    public string UnlockCode { get; }

    /// <summary>"0 of 2 used in the last 7 days".</summary>
    [ObservableProperty]
    public partial string UnlocksText { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    public void StartDayOffWait(DateTimeOffset askedAt)
    {
        _dayOffAskedAt = askedAt;
        IsWaiting = true;
    }

    /// <summary>Counts the day-off wait down. Runs every second.</summary>
    public void Refresh(DateTimeOffset now, int unlocksThisWeek)
    {
        UnlocksText = $"{unlocksThisWeek} of {PreWorkService.UnlocksPerWeek} used in the last 7 days";
        if (_dayOffAskedAt is not DateTimeOffset asked)
        {
            return;
        }

        TimeSpan left = asked + PreWorkService.DayOffWait - now;
        CanTakeDayOff = left <= TimeSpan.Zero;
        WaitText = CanTakeDayOff ? "Done planning? Your day off starts when you press it." : $"Day off in {(int)left.TotalMinutes}:{Math.Max(0, left.Seconds):00} · plan your day meanwhile";
    }

    [RelayCommand]
    private Task WorkDayAsync() => _service.AnswerWorkDayAsync();

    [RelayCommand]
    private Task DayOffAsync() => _service.AskDayOffAsync();

    /// <summary>"No, a day off" was a mistake: back to the question.</summary>
    [RelayCommand]
    private async Task CancelDayOffAsync()
    {
        await _service.CancelDayOffAsync();
        _dayOffAskedAt = null;
        IsWaiting = false;
    }

    [RelayCommand(CanExecute = nameof(CanTakeDayOff))]
    private Task TakeDayOffAsync() =>
        _service.TakeDayOffAsync(Plans.Split('\n'), AddComeback ?? (_ => Task.CompletedTask));

    /// <summary>Keeps a planned line as a come-back; set by the app.</summary>
    public Func<string, Task>? AddComeback { get; set; }

    [RelayCommand]
    private void ToggleUnlock()
    {
        ShowsUnlock = !ShowsUnlock;
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        Message = await _service.EmergencyUnlockAsync(Reason, TypedCode, UnlockCode);
    }
}
