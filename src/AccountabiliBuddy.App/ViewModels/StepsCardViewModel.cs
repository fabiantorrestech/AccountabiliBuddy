using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's daily steps card: a ring that fills to the goal, "6,240 of 8,000 steps" and "Updated 2:14 PM", from the counts a paired
/// phone shares (the phone reads Health Connect while its app is open, so the time is real, not live). Shows only once a phone's file
/// carries steps; "Hide" collapses it to one line with "Show", on this PC only.
/// </summary>
public sealed partial class StepsCardViewModel : ObservableObject
{
    private readonly DailyStepsService _steps;
    private readonly TimeProvider _time;

    public StepsCardViewModel(DailyStepsService steps, TimeProvider time)
    {
        _steps = steps;
        _time = time;
        CountText = string.Empty;
        UpdatedText = string.Empty;
        _steps.Changed += (_, _) => Refresh();
        // The day can roll over with no new file: look again now and then.
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        Refresh();
    }

    /// <summary>A phone shares steps and the card isn't hidden.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>A phone shares steps but the card is hidden: one quiet line offers "Show".</summary>
    [ObservableProperty]
    public partial bool IsHidden { get; set; }

    /// <summary>0 to 1, for the ring.</summary>
    [ObservableProperty]
    public partial double Fraction { get; set; }

    /// <summary>"6,240 of 8,000 steps", or "No steps counted yet today".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>"Updated 2:14 PM" (with the day when it isn't today), in 12-hour time.</summary>
    [ObservableProperty]
    public partial string UpdatedText { get; set; }

    [ObservableProperty]
    public partial bool IsMet { get; set; }

    public void Refresh()
    {
        SyncSteps? steps = _steps.Current;
        IsVisible = steps is not null && !_steps.Hidden;
        IsHidden = steps is not null && _steps.Hidden;
        if (steps is null)
        {
            return;
        }

        DateTime now = _time.GetLocalNow().DateTime;
        int? count = _steps.CountOn(DateOnly.FromDateTime(now));
        CultureInfo culture = CultureInfo.CurrentCulture;
        CountText = count is int counted
            ? string.Create(culture, $"{counted:N0} of {steps.Goal:N0} steps")
            : "No steps counted yet today";
        Fraction = count is int value ? Math.Clamp((double)value / steps.Goal, 0, 1) : 0;
        IsMet = count is int met && met >= steps.Goal;

        DateTime readAt = TimeZoneInfo.ConvertTime(steps.ReadAt, _time.LocalTimeZone).DateTime;
        string clock = readAt.ToString("h:mm tt", CultureInfo.InvariantCulture);
        UpdatedText = readAt.Date == now.Date ? $"Updated {clock}" : $"Updated {readAt:ddd MMM d}, {clock}";
    }

    [RelayCommand]
    private Task HideAsync() => _steps.SetHiddenAsync(true);

    [RelayCommand]
    private Task ShowAsync() => _steps.SetHiddenAsync(false);
}
