using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A day of the week in the Day plan tab: on = it follows the Workdays template.</summary>
public sealed partial class WorkdayChip : ObservableObject
{
    public WorkdayChip(DayOfWeek day, bool isOn)
    {
        Day = day;
        Label = day.ToString()[..3];
        IsOn = isOn;
    }

    public DayOfWeek Day { get; }

    /// <summary>"Mon", "Tue"…</summary>
    public string Label { get; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }
}
