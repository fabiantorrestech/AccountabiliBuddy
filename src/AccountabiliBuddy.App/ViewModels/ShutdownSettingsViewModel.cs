using System.ComponentModel;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings → Your day → "Shutdown routine": on or off, a switch per step, the closing line and the reminder minutes. Each change saves
/// at once. None of it is a guard (the routine enforces nothing), so nothing here waits or is locked.
/// </summary>
public sealed partial class ShutdownSettingsViewModel : ObservableObject
{
    private readonly ShutdownService _shutdown;
    private bool _loading;

    public ShutdownSettingsViewModel(ShutdownService shutdown)
    {
        _shutdown = shutdown;
        ShutdownSettings settings = shutdown.Settings;
        _loading = true;
        IsOn = settings.Enabled;
        CaptureOn = settings.Capture;
        UnfinishedOn = settings.Unfinished;
        LookAheadOn = settings.LookAhead;
        PlanOn = settings.PlanTomorrow;
        ReviewOn = settings.Review;
        ClosingLine = settings.ClosingLine;
        ReminderMinutes = settings.ReminderMinutes;
        _loading = false;
    }

    public static decimal MaxReminderMinutes => ShutdownSettings.MaxReminderMinutes;

    public static int MaxClosingLineLength => ShutdownSettings.MaxClosingLineLength;

    /// <summary>Off: no card at the end of work and no reminder. "Start shutdown" on Today stays.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial bool CaptureOn { get; set; }

    [ObservableProperty]
    public partial bool UnfinishedOn { get; set; }

    [ObservableProperty]
    public partial bool LookAheadOn { get; set; }

    [ObservableProperty]
    public partial bool PlanOn { get; set; }

    [ObservableProperty]
    public partial bool ReviewOn { get; set; }

    /// <summary>What the closing screen and Today say; blank = "Work over for today."</summary>
    [ObservableProperty]
    public partial string ClosingLine { get; set; }

    /// <summary>Minutes before work ends the reminder comes; 0 = none.</summary>
    [ObservableProperty]
    public partial decimal? ReminderMinutes { get; set; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is nameof(IsOn) or nameof(CaptureOn) or nameof(UnfinishedOn) or nameof(LookAheadOn) or nameof(PlanOn) or nameof(ReviewOn)
            or nameof(ClosingLine) or nameof(ReminderMinutes))
        {
            _ = _shutdown.SaveSettingsAsync(new ShutdownSettings(
                IsOn, CaptureOn, UnfinishedOn, LookAheadOn, PlanOn, ReviewOn, ClosingLine ?? string.Empty, (int)(ReminderMinutes ?? 0)));
        }
    }
}
