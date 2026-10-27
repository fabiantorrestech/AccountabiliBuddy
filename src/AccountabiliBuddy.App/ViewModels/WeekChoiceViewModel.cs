using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One button of the "This is week N of M" switch on Today: a week number of the cycle, and whether this week is it (ADR 47).</summary>
public sealed partial class WeekChoiceViewModel : ObservableObject
{
    public WeekChoiceViewModel(int number, bool isCurrent)
    {
        Number = number;
        IsCurrent = isCurrent;
    }

    /// <summary>The week of the cycle, from 1.</summary>
    public int Number { get; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}
