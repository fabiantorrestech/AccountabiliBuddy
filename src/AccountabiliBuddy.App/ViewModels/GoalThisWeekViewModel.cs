using System.Globalization;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The This week card at the top of the Goals list (round 2, change note §4a; fixed, the list scrolls under it): "5 of 8 on track",
/// an amber "1 behind" pill that jumps to the goal, "2 not due yet", a segmented bar (on track / behind / not due), and the weekly
/// review row (Snooze / Start). Paused goals leave every count (brief §5.4). The review row comes on the review day and time
/// (<see cref="SetReview"/> from <see cref="GoalReviewService.Row"/>); its dialog and snooze go through
/// <see cref="StartReviewRequested"/> and <see cref="SnoozeReviewRequested"/>.
/// </summary>
public sealed partial class GoalThisWeekViewModel : ObservableObject
{
    private readonly Action _jumpToBehind;

    public GoalThisWeekViewModel(Action jumpToBehind)
    {
        _jumpToBehind = jumpToBehind;
    }

    /// <summary>"Nov 8 – 14".</summary>
    [ObservableProperty]
    public partial string RangeText { get; set; } = string.Empty;

    /// <summary>"5 of 8".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int OnTrack { get; set; }

    /// <summary>Active (not paused) goals counted this week.</summary>
    [ObservableProperty]
    public partial int Total { get; set; }

    [ObservableProperty]
    public partial int Behind { get; set; }

    [ObservableProperty]
    public partial int NotDue { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<GoalWeekSegment> Segments { get; set; } = [];

    public string BehindText => $"{Behind} behind";

    public string NotDueText => $"{NotDue} not due yet";

    public bool HasBehind => Behind > 0;

    public bool HasNotDue => NotDue > 0;

    /// <summary>"Sundays · about 4 min, then the tidy-up" (the review's day follows the week start until Settings › Goals sets it).</summary>
    [ObservableProperty]
    public partial string ReviewNote { get; set; } = string.Empty;

    /// <summary>The review row shows: the review is due and not done (or its tidy-up is), no session runs, it isn't snoozed in AB mode.</summary>
    [ObservableProperty]
    public partial bool IsReviewShown { get; set; } = true;

    /// <summary>Snooze is offered (not on ProductaBuddy's quiet snoozed row).</summary>
    [ObservableProperty]
    public partial bool CanSnoozeReview { get; set; } = true;

    /// <summary>"Snooze the weekly review for 1 day".</summary>
    [ObservableProperty]
    public partial string SnoozeReviewTip { get; set; } = "Snooze the weekly review";

    /// <summary>The review row as <see cref="GoalReviewService.Row"/> says.</summary>
    public void SetReview(GoalReviewRow row)
    {
        IsReviewShown = row.IsShown;
        CanSnoozeReview = row.CanSnooze;
        ReviewNote = row.Note;
        SnoozeReviewTip = row.SnoozeTip;
    }

    /// <summary>R5b hook: Start opens the weekly goals review dialog.</summary>
    public event EventHandler? StartReviewRequested;

    /// <summary>R5b hook: Snooze (1 or 2 days, per mode).</summary>
    public event EventHandler? SnoozeReviewRequested;

    /// <summary>
    /// The counts from the goals' facts: every live, not paused goal is on track, behind or not due yet this week (a goal paused
    /// today is left out; brief §5.4).
    /// </summary>
    public void Update(IEnumerable<GoalFacts> goals, DateOnly today, GoalWeekStart weekStart)
    {
        List<GoalFacts> active = goals.Where(facts => !facts.IsPaused && facts.Status != GoalPeriodStatus.Paused).ToList();
        Total = active.Count;
        OnTrack = active.Count(facts => facts.Status == GoalPeriodStatus.OnTrack);
        Behind = active.Count(facts => facts.Status == GoalPeriodStatus.Behind);
        NotDue = active.Count(facts => facts.Status == GoalPeriodStatus.NotDueYet);
        CountText = $"{OnTrack} of {Total}";
        GoalPeriodSpan week = GoalPeriods.WeekOf(today, weekStart);
        RangeText = week.Start.Month == week.End.Month
            ? $"{week.Start.ToString("MMM", CultureInfo.InvariantCulture)} {week.Start.Day} – {week.End.Day}"
            : $"{week.Start.ToString("MMM", CultureInfo.InvariantCulture)} {week.Start.Day} – {week.End.ToString("MMM", CultureInfo.InvariantCulture)} {week.End.Day}";
        Segments =
        [
            .. Enumerable.Repeat(new GoalWeekSegment(true, false, false), OnTrack),
            .. Enumerable.Repeat(new GoalWeekSegment(false, true, false), Behind),
            .. Enumerable.Repeat(new GoalWeekSegment(false, false, true), NotDue),
        ];
        string reviewDay = GoalPeriods.FirstDay(weekStart).ToString() + "s";
        ReviewNote = $"{reviewDay} · about 4 min, then the tidy-up";
        OnPropertyChanged(nameof(BehindText));
        OnPropertyChanged(nameof(NotDueText));
        OnPropertyChanged(nameof(HasBehind));
        OnPropertyChanged(nameof(HasNotDue));
    }

    /// <summary>"Sundays · about 4 min, then the tidy-up" from Settings › Goals' review day and its tidy-up-after option.</summary>
    public static string ReviewText(DayOfWeek day, bool tidyUpAfter)
    {
        return tidyUpAfter ? $"{day}s · about 4 min, then the tidy-up" : $"{day}s · about 4 min";
    }

    [RelayCommand]
    private void JumpToBehind()
    {
        _jumpToBehind();
    }

    [RelayCommand]
    private void StartReview()
    {
        StartReviewRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SnoozeReview()
    {
        SnoozeReviewRequested?.Invoke(this, EventArgs.Empty);
    }
}
