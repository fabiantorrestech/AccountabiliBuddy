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

    private readonly Func<DateTime?> _phoneAlarm;

    /// <param name="blocks">Every device's blocks: the block, its device's name, whether it's on now.</param>
    /// <param name="phoneAlarm">The paired phone's next alarm (ADR 23), for "hours of sleep + my alarm".</param>
    public PlanDayViewModel(ConfigurationService configuration, SessionController sessions, IReadOnlyList<(BlockRef Block, string Device, bool On)> blocks, DateOnly today,
        Func<DateTime?>? phoneAlarm = null)
    {
        _configuration = configuration;
        _sessions = sessions;
        _phoneAlarm = phoneAlarm ?? (() => null);
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

    partial void OnBedtimeChanged(TimeSpan? value) => OnPropertyChanged(nameof(SleepText));

    partial void OnWakeChanged(TimeSpan? value) => OnPropertyChanged(nameof(SleepText));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepText))]
    public partial TimeSpan? Bedtime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepText))]
    public partial TimeSpan? Wake { get; set; }

    [ObservableProperty]
    public partial bool WakeByAlarm { get; set; }

    // How the night before is set: bed and wake times; hours + a wake time; hours + the phone's alarm; or left blank.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRange), nameof(IsHoursAndWake), nameof(IsHoursAndAlarm), nameof(IsBlank), nameof(ShowsHours), nameof(ShowsWake), nameof(SleepText))]
    public partial string SleepMode { get; set; } = "range";

    public bool IsRange { get => SleepMode == "range"; set { if (value) SleepMode = "range"; } }
    public bool IsHoursAndWake { get => SleepMode == "hoursWake"; set { if (value) SleepMode = "hoursWake"; } }
    public bool IsHoursAndAlarm { get => SleepMode == "hoursAlarm"; set { if (value) SleepMode = "hoursAlarm"; } }
    public bool IsBlank { get => SleepMode == "blank"; set { if (value) SleepMode = "blank"; } }
    public bool ShowsHours => IsHoursAndWake || IsHoursAndAlarm;
    public bool ShowsWake => IsRange || IsHoursAndWake;

    /// <summary>Hours of sleep (4 to 12, a quarter hour apart).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepText))]
    public partial decimal? SleepHours { get; set; } = 8m;

    // The phone's alarm counts for the first chosen morning when it rings that day.
    private TimeOnly? AlarmForFirstDate() =>
        _phoneAlarm() is DateTime alarm && Dates.Count > 0 && DateOnly.FromDateTime(alarm) == Dates[0] ? TimeOnly.FromDateTime(alarm) : null;

    private SleepTimes? ChosenNight()
    {
        TimeSpan hours = TimeSpan.FromHours((double)(SleepHours ?? 8m));
        TimeOnly? wake = Wake is TimeSpan w ? TimeOnly.FromTimeSpan(w) : null;
        return SleepMode switch
        {
            "hoursWake" when wake is TimeOnly up => new SleepTimes(up.Add(-hours), up),
            "hoursAlarm" => (AlarmForFirstDate() ?? wake) is TimeOnly up ? new SleepTimes(up.Add(-hours), up, WakeByAlarm: true) : null,
            "blank" => null,
            _ => Bedtime is TimeSpan bed && wake is TimeOnly end ? new SleepTimes(TimeOnly.FromTimeSpan(bed), end, WakeByAlarm) : null,
        };
    }

    public string SleepText
    {
        get
        {
            if (SleepMode == "blank")
            {
                return "No sleep planned for the night before (not recommended): no bedtime reminders or sleep count for those nights.";
            }

            if (ChosenNight() is not SleepTimes night)
            {
                return "No night planned.";
            }

            TimeSpan length = night.Wake.ToTimeSpan() - night.Bedtime.ToTimeSpan();
            if (length <= TimeSpan.Zero)
            {
                length += TimeSpan.FromDays(1);
            }

            string alarm = SleepMode == "hoursAlarm"
                ? AlarmForFirstDate() is TimeOnly up ? $" (your phone's alarm: {Time(up)})" : " (no alarm set for that morning yet; it follows your alarm once set)"
                : "";
            return $"Bed by {Time(night.Bedtime)}, up at {Time(night.Wake)}{alarm} · {(int)length.TotalHours} h {length.Minutes:00} m of sleep";
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
        SleepTimes? sleep = ChosenNight();

        BlockRules rules = template.Rules;
        TimeOnly reenable = TimeOnly.FromTimeSpan(ReenableAt ?? TimeSpan.Zero);
        List<BlockRef> lifted = Blocks.Where(row => row.Lift).Select(row => row.Block).ToList();
        IReadOnlyList<BlockLift> lifts = lifted.Count == 0
            ? rules.Lifts
            : new[] { new BlockLift(TimeOnly.FromTimeSpan(LiftFrom ?? TimeSpan.Zero), TimeOnly.FromTimeSpan(LiftTo ?? ReenableAt ?? TimeSpan.Zero), LiftAuto ? BlockLift.Auto : BlockLift.Allow, lifted) };
        var blocks = rules with { ReenableAt = reenable, WarnMinutes = HeadsUp ? rules.WarnMinutes ?? 5 : null, Lifts = lifts };
        return new DayPlanRequest(Dates, IsOffDay, sleep, blocks, Note, NoSleep: SleepMode == "blank");
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
            request.NoSleep ? "The night before: no sleep planned" : $"The night before: {SleepText}",
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
