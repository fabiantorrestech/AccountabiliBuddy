using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Plan a day" (ADR 37): four steps that make one-off days. 1) adjusted workday or off-day, and the dates;
/// 2) the night before (bed by, up at, follow my alarm); 3) blocks to lift on every device, the lift window, Allow or
/// Auto, the re-enable time and the heads-up; 4) a summary and Save. The rules are <see cref="DayPlanner"/>'s; saving
/// is locked during a focus session like the Day plan tab.
/// </summary>
public sealed partial class PlanDayViewModel : ObservableObject
{
    private static readonly CultureInfo Twelve = CultureInfo.InvariantCulture;
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    /// <param name="blocks">Every device's blocks: the block, its device's name, whether it's on now.</param>
    public PlanDayViewModel(ConfigurationService configuration, SessionController sessions, IReadOnlyList<(BlockRef Block, string Device, bool On)> blocks, DateOnly today)
    {
        _configuration = configuration;
        _sessions = sessions;
        Today = today;
        Dates = new List<DateOnly> { today.AddDays(1) };
        foreach ((BlockRef block, string device, bool on) in blocks)
        {
            Blocks.Add(new PlanDayBlockRow(block, device, on));
        }

        LoadDefaults();
    }

    public DateOnly Today { get; }

    /// <summary>The chosen dates (the window's calendar sets them).</summary>
    public IReadOnlyList<DateOnly> Dates
    {
        get;
        set
        {
            field = value.Distinct().Order().ToList();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DatesText));
            NextCommand.NotifyCanExecuteChanged();
        }
    } = Array.Empty<DateOnly>();

    public string DatesText => Dates.Count == 0 ? "Pick at least one date." : string.Join(", ", Dates.Select(DateText));

    public ObservableCollection<PlanDayBlockRow> Blocks { get; } = new();

    public bool HasBlocks => Blocks.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(StepTitle), nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial int Step { get; set; } = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool CanGoBack => Step > 1;

    public string StepTitle => Step switch
    {
        1 => "1 of 4 · What kind of day?",
        2 => "2 of 4 · Sleep the night before",
        3 => "3 of 4 · Blocks",
        _ => "4 of 4 · Check and save",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkday))]
    public partial bool IsOffDay { get; set; }

    public bool IsWorkday
    {
        get => !IsOffDay;
        set => IsOffDay = !value;
    }

    partial void OnIsOffDayChanged(bool value) => LoadDefaults();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepText))]
    public partial TimeSpan? Bedtime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepText))]
    public partial TimeSpan? Wake { get; set; }

    [ObservableProperty]
    public partial bool WakeByAlarm { get; set; }

    public string SleepText
    {
        get
        {
            if (Bedtime is not TimeSpan bed || Wake is not TimeSpan wake)
            {
                return "No night planned.";
            }

            TimeSpan length = wake - bed;
            if (length <= TimeSpan.Zero)
            {
                length += TimeSpan.FromDays(1);
            }

            return $"{(int)length.TotalHours} h {length.Minutes:00} m of sleep";
        }
    }

    [ObservableProperty]
    public partial TimeSpan? LiftFrom { get; set; } = TimeSpan.Zero;

    [ObservableProperty]
    public partial TimeSpan? LiftTo { get; set; } = TimeSpan.Zero;

    [ObservableProperty]
    public partial bool LiftAuto { get; set; }

    [ObservableProperty]
    public partial TimeSpan? ReenableAt { get; set; } = TimeSpan.Zero;

    [ObservableProperty]
    public partial bool HeadsUp { get; set; } = true;

    [ObservableProperty]
    public partial string? Note { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public bool IsLocked => _sessions.IsEnforcing;

    /// <summary>Saved: the window closes.</summary>
    public event EventHandler? Done;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        Step++;
        if (Step == 4)
        {
            Summary = BuildSummary();
        }
    }

    private bool CanGoNext() => Step < 4 && (Step != 1 || Dates.Count > 0);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    [RelayCommand]
    private async Task Save()
    {
        if (_sessions.IsEnforcing)
        {
            StatusMessage = "The day plan is locked while a focus session you started is running.";
            return;
        }

        if (Dates.Count == 0)
        {
            StatusMessage = "Pick at least one date.";
            Step = 1;
            return;
        }

        DayPlanRequest request = Request();
        await _configuration.UpdateAsync(document => document with { DayPlan = DayPlanner.Apply(document.DayPlan, request) });
        StatusMessage = _configuration.DayPlanWaitText;
        if (StatusMessage is null)
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
    }

    private DayPlanRequest Request()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        DayTemplate template = IsOffDay ? plan.OtherDay : plan.Workday;
        SleepTimes? sleep = Bedtime is TimeSpan bed && Wake is TimeSpan wake
            ? new SleepTimes(TimeOnly.FromTimeSpan(bed), TimeOnly.FromTimeSpan(wake), WakeByAlarm)
            : null;

        BlockRules rules = template.Rules;
        TimeOnly reenable = TimeOnly.FromTimeSpan(ReenableAt ?? TimeSpan.Zero);
        List<BlockRef> lifted = Blocks.Where(row => row.Lift).Select(row => row.Block).ToList();
        IReadOnlyList<BlockLift> lifts = lifted.Count == 0
            ? rules.Lifts
            : new[] { new BlockLift(TimeOnly.FromTimeSpan(LiftFrom ?? TimeSpan.Zero), TimeOnly.FromTimeSpan(LiftTo ?? ReenableAt ?? TimeSpan.Zero), LiftAuto ? BlockLift.Auto : BlockLift.Allow, lifted) };
        var blocks = rules with { ReenableAt = reenable, WarnMinutes = HeadsUp ? rules.WarnMinutes ?? 5 : null, Lifts = lifts };
        return new DayPlanRequest(Dates, IsOffDay, sleep, blocks, Note);
    }

    private void LoadDefaults()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        DayTemplate template = IsOffDay ? plan.OtherDay : plan.Workday;
        SleepTimes? night = DayPlanSleep.SleepOf(template, plan);
        Bedtime = night?.Bedtime.ToTimeSpan();
        Wake = night?.Wake.ToTimeSpan();
        WakeByAlarm = night?.WakeByAlarm ?? false;
        BlockRules rules = template.Rules;
        ReenableAt = rules.ReenableAt.ToTimeSpan();
        LiftTo = rules.ReenableAt.ToTimeSpan();
        HeadsUp = rules.WarnMinutes is > 0;
    }

    private string BuildSummary()
    {
        DayPlanRequest request = Request();
        var lines = new List<string>
        {
            $"{(request.OffDay ? "Off-day" : "Adjusted workday")} on {DatesText}",
            request.Sleep is SleepTimes sleep
                ? $"The night before: bed by {Time(sleep.Bedtime)}, up at {Time(sleep.Wake)}{(sleep.WakeByAlarm ? " (or your alarm)" : "")} · {SleepText}"
                : "No night planned",
        };
        BlockRules blocks = request.Blocks!;
        List<BlockRef> lifted = Blocks.Where(row => row.Lift).Select(row => row.Block).ToList();
        if (lifted.Count > 0)
        {
            BlockLift lift = blocks.Lifts[0];
            lines.Add($"Lifted {Time(lift.Start)}–{Time(lift.End)} ({(lift.Mode == BlockLift.Auto ? "turned off for you" : "you may turn them off")}): {string.Join(", ", lifted.Select(b => b.Name))}");
        }

        lines.Add($"Blocks you turned off come back on at {Time(blocks.ReenableAt)}{(blocks.WarnMinutes is int m ? $", with a heads-up {m} min before" : "")}");
        IReadOnlyList<DateOnly> clashes = DayPlanner.Clashes(_configuration.Current.DayPlan, Dates);
        if (clashes.Count > 0)
        {
            lines.Add($"Replaces the plan already set for {string.Join(", ", clashes.Select(DateText))}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string DateText(DateOnly date) => date.ToString("ddd M/d", Twelve);

    private static string Time(TimeOnly time) => time == TimeOnly.MinValue ? "midnight" : time.ToString("h:mm tt", Twelve);
}
