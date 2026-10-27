using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The "This week is A / B" switch on Today (ADR 47), shown while alternating weeks are on. It moves the Monday week B
/// starts on, so it is a plan change like any other: stricter applies at once, looser (it frees today's work) waits, and
/// it is locked during a focus session the user started.
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

    /// <summary>Alternating weeks are on.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeekA), nameof(Text))]
    public partial bool IsWeekB { get; set; }

    public bool IsWeekA => !IsWeekB;

    /// <summary>"This week is A" or "This week is B".</summary>
    public string Text => IsWeekB ? "This week is B" : "This week is A";

    /// <summary>The buttons can't change the plan: a focus session you started is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetWeekACommand), nameof(SetWeekBCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>What happened to the last switch: a change that frees today's work waits; null when it applied.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    private bool CanSwitch => !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private Task SetWeekAAsync() => SetWeekAsync(weekB: false);

    [RelayCommand(CanExecute = nameof(CanSwitch))]
    private Task SetWeekBAsync() => SetWeekAsync(weekB: true);

    private async Task SetWeekAsync(bool weekB)
    {
        if (IsWeekB == weekB)
        {
            return;
        }

        DateOnly today = _today();
        await _configuration.UpdateAsync(document => document with { DayPlan = WeekSwitch.WithThisWeek(document.DayPlan, today, weekB) });
        Message = _configuration.IsReadOnly ? "Not saved: settings are read-only (see banner)." : _configuration.DayPlanWaitText;
        Refresh();
    }

    private void Refresh()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        IsVisible = WeekSwitch.IsAlternating(plan);
        IsWeekB = plan.Week.IsWeekB(_today());
        IsLocked = _sessions.IsEnforcing;
    }
}
