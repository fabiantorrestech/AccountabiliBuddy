using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day's button in an alarm's days (Mon … Sun); none ticked = once.</summary>
public sealed partial class DayToggleViewModel : ObservableObject
{
    public DayToggleViewModel(DayOfWeek day, bool isOn = false)
    {
        Day = day;
        Label = day.ToString()[..3];
        IsOn = isOn;
    }

    public DayOfWeek Day { get; }

    /// <summary>"Mon".</summary>
    public string Label { get; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Monday first.</summary>
    public static IReadOnlyList<DayToggleViewModel> Week(IEnumerable<DayOfWeek>? on = null)
    {
        var chosen = new HashSet<DayOfWeek>(on ?? []);
        DayOfWeek[] order = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        return order.Select(day => new DayToggleViewModel(day, chosen.Contains(day))).ToList();
    }
}
