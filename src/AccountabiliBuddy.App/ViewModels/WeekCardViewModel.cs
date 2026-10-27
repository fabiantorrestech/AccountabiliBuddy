using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "This is week N of M" switch on Today (ADR 47), shown while the plan's weeks repeat in a cycle of more than one week.
/// It moves the Monday week 1 starts on by whole weeks, so it is a plan change like any other: stricter applies at once,
/// looser (it frees today's work) waits, and it is locked during a focus session the user started.
/// </summary>
public sealed partial class WeekCardViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly Func<DateOnly> _today;

    public WeekCardViewModel(ConfigurationService configuration, SessionController sessions, Func<DateOnly> today)
    {
        _configuration = configuration;
        _sessions = sessions;
        _today = today;
        Refresh();
        _configuration.Changed += (_, _) => Refresh();
        _sessions.StateChanged += (_, _) => Refresh();
        // The week turns over on a Monday, whatever else happens.
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }

    /// <summary>The weeks repeat in a cycle of more than one week.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>The week of the cycle this week is, from 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial int WeekNumber { get; set; }

    /// <summary>How many weeks the cycle has.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial int WeekCount { get; set; }

    /// <summary>"This is week 2 of 3".</summary>
    public string Text => $"This is week {WeekNumber} of {WeekCount}";

    /// <summary>One button per week of the cycle.</summary>
    public ObservableCollection<WeekChoiceViewModel> Choices { get; } = new();

    /// <summary>The buttons can't change the plan: a focus session you started is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetWeekCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>What happened to the last switch: a change that frees today's work waits; null when it applied.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    private bool CanSwitch => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private async Task SetWeekAsync(int week)
    {
        if (WeekNumber == week)
        {
            return;
        }

        DateOnly today = _today();
        await _configuration.UpdateAsync(document => document with { DayPlan = WeekSwitch.WithThisWeek(document.DayPlan, today, week) });
        Message = _configuration.IsReadOnly ? "Not saved: settings are read-only (see banner)." : _configuration.DayPlanWaitText;
        Refresh();
    }

    private void Refresh()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        IsVisible = WeekSwitch.IsRepeating(plan);
        WeekCount = plan.Cycle.Weeks.Count;
        WeekNumber = plan.Cycle.WeekNumberFor(_today());
        IsLocked = _sessions.IsEnforcing;

        if (Choices.Count != WeekCount)
        {
            Choices.Clear();
            for (int number = 1; number <= WeekCount; number++)
            {
                Choices.Add(new WeekChoiceViewModel(number, number == WeekNumber));
            }
        }
        else
        {
            foreach (WeekChoiceViewModel choice in Choices)
            {
                choice.IsCurrent = choice.Number == WeekNumber;
            }
        }
    }
}
