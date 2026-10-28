using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day of the Plan page's week strip: weekday, date number and a short day type ("Work", "Off").</summary>
public sealed partial class PlanDayCellViewModel : ObservableObject
{
    private readonly Action<DateOnly> _select;

    public PlanDayCellViewModel(DateOnly date, string weekday, string typeText, bool isToday, bool isSelected, Action<DateOnly> select)
    {
        Date = date;
        WeekdayText = weekday;
        DayNumber = date.Day.ToString();
        TypeText = typeText;
        IsToday = isToday;
        IsSelected = isSelected;
        _select = select;
    }

    public DateOnly Date { get; }

    public string WeekdayText { get; }

    public string DayNumber { get; }

    public string TypeText { get; }

    /// <summary>Today is drawn bold.</summary>
    public bool IsToday { get; }

    /// <summary>The day the timeline shows (a Surface2 rounded cell).</summary>
    public bool IsSelected { get; }

    /// <summary>What a screen reader says: "Tuesday 27, Work".</summary>
    public string AccessibleName => $"{Date:dddd d MMMM}, {TypeText}";

    [RelayCommand]
    private void Select() => _select(Date);
}
