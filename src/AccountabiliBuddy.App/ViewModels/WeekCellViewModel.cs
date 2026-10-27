using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One cell of the week grid: the day type one weekday follows in one week of the cycle (ADR 47); null = follows Off day.</summary>
public sealed partial class WeekCellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial PlanTemplateEditorViewModel? Template { get; set; }
}
