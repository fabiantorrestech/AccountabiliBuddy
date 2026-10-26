using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Settings → Your day → "Lunch length on workdays": a break's length, not a block, so it saves at once.</summary>
public sealed partial class LunchSettingsViewModel : ObservableObject
{
    private readonly LunchService _lunch;

    public LunchSettingsViewModel(LunchService lunch)
    {
        _lunch = lunch;
        LengthMinutes = (decimal)lunch.Length.TotalMinutes;
        LengthText = LunchService.FormatLength(lunch.Length);
    }

    public static decimal MinMinutes => LunchService.MinLengthMinutes;

    public static decimal MaxMinutes => LunchService.MaxLengthMinutes;

    public static decimal StepMinutes => LunchService.LengthStepMinutes;

    /// <summary>Minutes, 15-120 in steps of 5 (the box rounds what's typed).</summary>
    [ObservableProperty]
    public partial decimal? LengthMinutes { get; set; }

    /// <summary>The length as "1 h 5 m".</summary>
    [ObservableProperty]
    public partial string LengthText { get; set; }

    partial void OnLengthMinutesChanged(decimal? value)
    {
        if (value is not decimal minutes)
        {
            return;
        }

        _ = SaveAsync((int)minutes);
    }

    private async Task SaveAsync(int minutes)
    {
        await _lunch.SetLengthAsync(minutes);
        LengthText = LunchService.FormatLength(_lunch.Length);
    }
}
