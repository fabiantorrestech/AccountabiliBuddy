using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's "Your workdays" page (ADR 47, C 15): which weekdays are workdays and, for them, either just a
/// work window ("9:00 AM to 5:00 PM", enough to make the guards work) or a budget of productive hours, or the timeline
/// they already have. It edits the "Workday" day type and week 1 of the cycle; everything else is the Day plan tab's. The save goes
/// through <see cref="ConfigurationService"/>, so a change that frees today's work waits there.
/// </summary>
public sealed partial class WorkdaysSetupViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;

    public WorkdaysSetupViewModel(ConfigurationService configuration)
    {
        _configuration = configuration;
        foreach (DayOfWeek day in DayPlan.MondayFirst)
        {
            Days.Add(new WorkdayChip(day, isOn: false));
        }
    }

    /// <summary>Monday first; on = the day follows the Workday type in week 1.</summary>
    public ObservableCollection<WorkdayChip> Days { get; } = new();

    /// <summary>0 = keep the timeline the Workday type has, 1 = just a work window, 2 = a budget of hours.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeepTimeline), nameof(IsWorkWindow), nameof(IsBudget))]
    public partial int Choice { get; set; }

    /// <summary>The Workday type has a timeline of its own with work in it (so keeping it is an option).</summary>
    [ObservableProperty]
    public partial bool HasTimeline { get; set; }

    [ObservableProperty]
    public partial TimeSpan? WindowStart { get; set; } = new TimeSpan(9, 0, 0);

    [ObservableProperty]
    public partial TimeSpan? WindowEnd { get; set; } = new TimeSpan(17, 0, 0);

    [ObservableProperty]
    public partial decimal? BudgetHours { get; set; } = 6;

    /// <summary>What happened on the last save (a change waiting, a problem); null when there is nothing to say.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    public bool IsKeepTimeline
    {
        get => Choice == 0;
        set
        {
            if (value)
            {
                Choice = 0;
            }
        }
    }

    public bool IsWorkWindow
    {
        get => Choice == 1;
        set
        {
            if (value)
            {
                Choice = 1;
            }
        }
    }

    public bool IsBudget
    {
        get => Choice == 2;
        set
        {
            if (value)
            {
                Choice = 2;
            }
        }
    }

    /// <summary>Fills the page from the saved plan.</summary>
    public void Load()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        DayTemplate workday = plan.Workday;
        foreach (WorkdayChip chip in Days)
        {
            chip.IsOn = plan.Workdays.Contains(chip.Day);
        }

        HasTimeline = workday.Mode == DayMode.Schedule && workday.WorkWindow is null
                      && workday.Parts.Any(part => plan.Category(part.CategoryId)?.Behavior.IsWork() == true);
        if (workday.WorkWindow is WorkWindow window)
        {
            WindowStart = window.Start.ToTimeSpan();
            WindowEnd = window.End.ToTimeSpan();
        }

        if (workday.BudgetMinutes is int minutes)
        {
            BudgetHours = (decimal)(minutes / 60.0);
        }

        Choice = workday.Mode == DayMode.Budget ? 2 : HasTimeline ? 0 : 1;
        Message = null;
    }

    /// <summary>Saves the page; false (with <see cref="Message"/>) when it couldn't be saved.</summary>
    public async Task<bool> SaveAsync()
    {
        DayPlan current = _configuration.Current.DayPlan;
        DayPlan plan = Build(current);
        if (DayPlanVersion.Of(plan) == DayPlanVersion.Of(current))
        {
            Message = null;
            return true;
        }

        if (DayPlanValidator.Validate(plan) is string problem)
        {
            Message = $"Not saved: {problem}";
            return false;
        }

        await _configuration.UpdateAsync(document => document with { DayPlan = Build(document.DayPlan) });
        if (_configuration.IsReadOnly)
        {
            Message = "Not saved: settings are read-only (see banner).";
            return false;
        }

        Message = _configuration.DayPlanWaitText;
        return true;
    }

    // The plan with the chosen workdays and the Workday type as asked: its work parts give way to a window or a budget.
    private DayPlan Build(DayPlan plan)
    {
        DayTemplate workday = plan.Workday;
        List<PlanPart> rest = workday.Parts.Where(part => plan.Category(part.CategoryId)?.Behavior.IsWork() != true).ToList();
        DayTemplate next = Choice switch
        {
            1 when WindowStart is TimeSpan start && WindowEnd is TimeSpan end => workday with
            {
                Mode = DayMode.Schedule,
                Parts = rest,
                WorkWindow = new WorkWindow(TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end)),
                BudgetMinutes = null,
            },
            2 => workday with
            {
                Mode = DayMode.Budget,
                Parts = rest,
                WorkWindow = null,
                BudgetMinutes = (int)Math.Round((double)(BudgetHours ?? 6) * 60),
            },
            _ => workday,
        };
        return plan.WithWorkday(next).WithWorkdays(Days.Where(chip => chip.IsOn).Select(chip => chip.Day));
    }
}
