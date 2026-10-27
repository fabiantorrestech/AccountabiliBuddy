using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One named day type in the Day plan tab (schema v12, ADR 47): its name and the editor of its mode, work window,
/// budget and parts. "Workday" and "Off day" can't be deleted: the first is what old plans call a workday, the second
/// is what any weekday that follows no type falls back to.
/// </summary>
public sealed partial class PlanTemplateEditorViewModel : ObservableObject
{
    public PlanTemplateEditorViewModel(PlanTemplate template, IReadOnlyList<DayCategory> categories)
    {
        Id = template.Id;
        Name = template.Name;
        Editor = new DayPlanEditorViewModel(template.Template, categories);
        Editor.Changed += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    public DayPlanEditorViewModel Editor { get; }

    /// <summary>The Workday and Off day types: kept, never deleted.</summary>
    public bool IsProtected => Id == DayPlan.WorkdayTemplateId || Id == DayPlan.OffDayTemplateId;

    public bool CanDelete => !IsProtected;

    /// <summary>"Schedule", "Work window 9:00 AM–5:00 PM", "Budget 6h 15m", "Free day": the line under the name in the list.</summary>
    public string Summary
    {
        get
        {
            DayTemplate template = Editor.ToTemplate();
            return template.Mode switch
            {
                DayMode.Budget => $"Budget {Hours(template.BudgetMinutes ?? 0)}",
                DayMode.Free => "Free day",
                _ when template.WorkWindow is WorkWindow window => $"Work window {Time(window.Start)}–{Time(window.End)}",
                _ => "Schedule",
            };
        }
    }

    public PlanTemplate ToTemplate()
    {
        return new PlanTemplate(Id, Name.Trim(), Editor.ToTemplate());
    }

    public void Load(PlanTemplate template)
    {
        Name = template.Name;
        Editor.Load(template.Template);
    }

    /// <summary>"6h", "6h 15m", "45m".</summary>
    public static string Hours(int minutes)
    {
        return minutes % 60 == 0 ? $"{minutes / 60}h" : minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60}m";
    }

    private static string Time(TimeOnly time)
    {
        return time.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }
}
