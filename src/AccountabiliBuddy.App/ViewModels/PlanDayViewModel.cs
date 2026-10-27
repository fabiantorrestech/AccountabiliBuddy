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
    private readonly Func<IReadOnlyList<(BlockRef Block, string Device, bool On)>> _loadBlocks;

    /// <param name="loadBlocks">Every device's blocks: the block, its device's name, whether it's on now (read again on refresh).</param>
    /// <param name="phoneAlarm">The paired phone's next alarm (ADR 23), for "hours of sleep + my alarm".</param>
    public PlanDayViewModel(ConfigurationService configuration, SessionController sessions, Func<IReadOnlyList<(BlockRef Block, string Device, bool On)>> loadBlocks,
        DateOnly today, Func<DateTime?>? phoneAlarm = null)
    {
        _configuration = configuration;
        _sessions = sessions;
        _loadBlocks = loadBlocks;
        _phoneAlarm = phoneAlarm ?? (() => null);
        Today = today;
        Dates = new List<DateOnly> { today.AddDays(1) };
        LoadDefaults();
        LoadBlocks();
    }

    /// <summary>Every device's blocks, by device (its time, then its blocks).</summary>
    public ObservableCollection<PlanDayDeviceGroup> Devices { get; } = new();

    /// <summary>Reads every device's blocks again, keeping what was set for the ones still there.</summary>
    [RelayCommand]
    private void RefreshBlocks() => LoadBlocks();

    // The template the answers start from: for the first chosen date (tomorrow before one is picked), what that date follows
    // as a workday (later weeks of the cycle and custom templates included) or the Off day template.
    private DayTemplate BaseTemplate(DayPlan plan)
    {
        return DayPlanner.BaseFor(plan, IsOffDay, Dates.Count > 0 ? Dates[0] : Today.AddDays(1));
    }

    private void LoadBlocks()
    {
        var old = Devices.SelectMany(group => group.Rows).ToDictionary(row => (row.Block.Device, row.Block.Kind, row.Block.Id));
        var oldTimes = Devices.ToDictionary(group => group.Device, group => group.AgreedTime);
        BlockRules rules = BaseTemplate(_configuration.Current.DayPlan).Rules;
        Devices.Clear();
        Blocks.Clear();
        foreach (var device in _loadBlocks().GroupBy(item => (Id: item.Block.Device, Name: item.Device)))
        {
            TimeSpan agreed = oldTimes.TryGetValue(device.Key.Id, out TimeSpan? kept) && kept is TimeSpan t ? t : rules.ReenableFor(device.Key.Id).ToTimeSpan();
            var group = new PlanDayDeviceGroup(device.Key.Id, device.Key.Name, device.Key.Id == BlockRef.Phone, agreed);
            foreach ((BlockRef block, string name, bool on) in device)
            {
                var row = new PlanDayBlockRow(block, name, on);
                if (old.TryGetValue((block.Device, block.Kind, block.Id), out PlanDayBlockRow? previous))
                {
                    row.FollowsDevice = previous.FollowsDevice;
                    row.OwnTime = previous.OwnTime;
                    row.Lift = previous.Lift;
                }

                group.Rows.Add(row);
                Blocks.Add(row);
            }

            Devices.Add(group);
        }

        OnPropertyChanged(nameof(HasBlocks));
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
        3 => "3 of 4 · When blocks come back on",
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
        DayTemplate template = BaseTemplate(plan);
        SleepTimes? sleep = ChosenNight();

        BlockRules rules = template.Rules;
        List<BlockRef> lifted = Blocks.Where(row => row.Lift).Select(row => row.Block).ToList();
        IReadOnlyList<BlockLift> lifts = lifted.Count == 0
            ? rules.Lifts
            : new[] { new BlockLift(TimeOnly.FromTimeSpan(LiftFrom ?? TimeSpan.Zero), TimeOnly.FromTimeSpan(LiftTo ?? TimeSpan.Zero), LiftAuto ? BlockLift.Auto : BlockLift.Allow, lifted) };
        // Schema v11: each device's agreed time; unticked blocks with their own.
        List<DeviceReenable> deviceTimes = Devices.Select(group => new DeviceReenable(group.Device, TimeOnly.FromTimeSpan(group.AgreedTime ?? TimeSpan.Zero))).ToList();
        List<BlockReenable> blockTimes = Blocks.Where(row => !row.FollowsDevice)
            .Select(row => new BlockReenable(row.Block, TimeOnly.FromTimeSpan(row.OwnTime ?? TimeSpan.Zero))).ToList();
        var blocks = rules with
        {
            WarnMinutes = HeadsUp ? rules.WarnMinutes ?? 5 : null,
            Lifts = lifts,
            DeviceTimes = deviceTimes.Count > 0 ? deviceTimes : rules.DeviceTimes,
            BlockTimes = blockTimes.Count > 0 ? blockTimes : null,
        };
        return new DayPlanRequest(Dates, IsOffDay, sleep, blocks, Note, NoSleep: SleepMode == "blank");
    }

    private void LoadDefaults()
    {
        DayPlan plan = _configuration.Current.DayPlan;
        DayTemplate template = BaseTemplate(plan);
        SleepTimes? night = DayPlanSleep.SleepOf(template, plan);
        Bedtime = night?.Bedtime.ToTimeSpan();
        Wake = night?.Wake.ToTimeSpan();
        WakeByAlarm = night?.WakeByAlarm ?? false;
        BlockRules rules = template.Rules;
        ReenableAt = rules.ReenableAt.ToTimeSpan();
        LiftTo = rules.ReenableAt.ToTimeSpan();
        HeadsUp = rules.WarnMinutes is > 0;
        foreach (PlanDayDeviceGroup group in Devices)
        {
            group.AgreedTime = rules.ReenableFor(group.Device).ToTimeSpan();
        }
    }

    // The review as parts the window lays out as cards (not one block of text).
    public string ReviewKind { get; private set; } = "";
    public IReadOnlyList<string> ReviewDates { get; private set; } = Array.Empty<string>();
    public string? ReviewNote { get; private set; }
    public string ReviewNight { get; private set; } = "";
    public string ReviewNightDetail { get; private set; } = "";
    public bool ReviewNoSleep { get; private set; }
    public IReadOnlyList<PlanDayReviewDevice> ReviewDevices { get; private set; } = Array.Empty<PlanDayReviewDevice>();
    public string? ReviewLift { get; private set; }
    public string? ReviewHeadsUp { get; private set; }
    public string? ReviewClash { get; private set; }
    public bool HasReviewNote => ReviewNote is not null;
    public bool HasReviewLift => ReviewLift is not null;
    public bool HasReviewClash => ReviewClash is not null;

    private string BuildSummary()
    {
        DayPlanRequest request = Request();
        BlockRules blocks = request.Blocks!;
        ReviewKind = request.OffDay ? "Off-day" : "Adjusted workday";
        ReviewDates = Dates.Select(DateText).ToList();
        ReviewNote = string.IsNullOrWhiteSpace(Note) ? null : $"\u201c{Note.Trim()}\u201d";
        ReviewNoSleep = request.NoSleep;
        if (request.NoSleep)
        {
            ReviewNight = "No sleep planned";
            ReviewNightDetail = "No bedtime reminders or sleep count for those nights.";
        }
        else if (request.Sleep is SleepTimes night)
        {
            TimeSpan length = night.Wake.ToTimeSpan() - night.Bedtime.ToTimeSpan();
            if (length <= TimeSpan.Zero) length += TimeSpan.FromDays(1);
            ReviewNight = $"{Time(night.Bedtime)} \u2192 {Time(night.Wake)}";
            ReviewNightDetail = $"{(int)length.TotalHours} h {length.Minutes:00} m of sleep{(night.WakeByAlarm ? " \u00b7 follows your alarm" : "")}";
        }
        else
        {
            ReviewNight = "As your plan has it";
            ReviewNightDetail = "";
        }

        ReviewDevices = Devices.Select(group => new PlanDayReviewDevice(
            group.Name, group.IsPhone, Time(blocks.ReenableFor(group.Device)),
            group.Rows.Where(row => !row.FollowsDevice).Select(row => $"{row.Name}: {Time(TimeOnly.FromTimeSpan(row.OwnTime ?? TimeSpan.Zero))}").ToList())).ToList();
        List<BlockRef> lifted = Blocks.Where(row => row.Lift).Select(row => row.Block).ToList();
        ReviewLift = lifted.Count == 0 ? null
            : $"Lifted {Time(blocks.Lifts[0].Start)}\u2013{Time(blocks.Lifts[0].End)} ({(blocks.Lifts[0].Mode == BlockLift.Auto ? "turned off for you" : "you may turn them off")}): {string.Join(", ", lifted.Select(b => b.Name))}";
        ReviewHeadsUp = blocks.WarnMinutes is int m ? $"Heads-up {m} min before" : null;
        IReadOnlyList<DateOnly> clashes = DayPlanner.Clashes(_configuration.Current.DayPlan, Dates);
        ReviewClash = clashes.Count > 0 ? $"Replaces the plan already set for {string.Join(", ", clashes.Select(DateText))}" : null;
        foreach (string name in new[] { nameof(ReviewKind), nameof(ReviewDates), nameof(ReviewNote), nameof(ReviewNight), nameof(ReviewNightDetail), nameof(ReviewNoSleep),
                     nameof(ReviewDevices), nameof(ReviewLift), nameof(ReviewHeadsUp), nameof(ReviewClash), nameof(HasReviewNote), nameof(HasReviewLift), nameof(HasReviewClash) })
        {
            OnPropertyChanged(name);
        }

        return ReviewKind;
    }

    private static string DateText(DateOnly date) => date.ToString("ddd M/d", Twelve);

    private static string Time(TimeOnly time) => time == TimeOnly.MinValue ? "Midnight" : time.ToString("h:mm tt", Twelve);
}
