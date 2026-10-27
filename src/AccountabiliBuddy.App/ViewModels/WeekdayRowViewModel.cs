using System.Collections.ObjectModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the week grid: a weekday and the day type it follows in each week of the cycle (ADR 47).</summary>
public sealed class WeekdayRowViewModel
{
    public WeekdayRowViewModel(DayOfWeek day)
    {
        Day = day;
        Label = day.ToString();
    }

    public DayOfWeek Day { get; }

    /// <summary>"Monday", "Tuesday"…</summary>
    public string Label { get; }

    /// <summary>One cell per week of the cycle, week 1 first.</summary>
    public ObservableCollection<WeekCellViewModel> Cells { get; } = new();
}
