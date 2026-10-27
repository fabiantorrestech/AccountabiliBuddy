using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the week grid: a weekday and the day type it follows in week A and in week B (ADR 47).</summary>
public sealed partial class WeekdayRowViewModel : ObservableObject
{
    public WeekdayRowViewModel(DayOfWeek day)
    {
        Day = day;
        Label = day.ToString();
    }

    public DayOfWeek Day { get; }

    /// <summary>"Monday", "Tuesday"…</summary>
    public string Label { get; }

    [ObservableProperty]
    public partial PlanTemplateEditorViewModel? TemplateA { get; set; }

    [ObservableProperty]
    public partial PlanTemplateEditorViewModel? TemplateB { get; set; }
}
