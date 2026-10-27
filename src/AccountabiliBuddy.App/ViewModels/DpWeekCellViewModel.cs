using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One cell of the Setup DP Assistant's week grid: the day type one weekday follows in one week of the cycle.</summary>
public sealed partial class DpWeekCellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DpTemplateChoice? Template { get; set; }
}
