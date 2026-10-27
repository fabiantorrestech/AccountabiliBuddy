using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The away card (in front of everything, like the start-nudge card). Three questions: how long a side task runs (5, 10
/// or 15 minutes), "Back to the task?" after input while stepped away, and "Back to the task?" when a side task's time is up.
/// Nothing here shortens a wait or ends a session by itself: Back on track only resumes the countdown, +5 min needs a
/// reason (and from the second one a wait), and End session is the normal end-early flow.
/// </summary>
public sealed partial class AwayCardViewModel : ObservableObject
{
    private readonly AwayCardActions _actions;
    private readonly TimeProvider _time;
    private BypassPolicy? _policy;
    private int _minReasonLength;
    private DateTimeOffset _addFiveAskedAt;

    /// <param name="taskName">The current task's name; empty for "Back to your session?".</param>
    /// <param name="detail">The line under the title.</param>
    public AwayCardViewModel(AwayCardKind kind, string taskName, string detail, AwayCardActions actions, TimeProvider time)
    {
        Kind = kind;
        _actions = actions;
        _time = time;
        Title = kind == AwayCardKind.SideTaskLength
            ? "Side task"
            : taskName.Length > 0 ? $"Back to {taskName}?" : "Back to your session?";
        Detail = detail;
        Reason = string.Empty;
        AddFiveStatus = string.Empty;
    }

    public AwayCardKind Kind { get; }

    public string Title { get; }

    /// <summary>Under the title: a hint for the length question, else the growing "Away · 4:12" or "Side task · 6:12".</summary>
    [ObservableProperty]
    public partial string Detail { get; set; }

    /// <summary>The side-task kinds draw their time in amber.</summary>
    public bool IsSideTask => Kind != AwayCardKind.BackOnTrack;

    public bool ShowsLengths => Kind == AwayCardKind.SideTaskLength;

    public bool ShowsBackButtons => Kind == AwayCardKind.BackOnTrack;

    /// <summary>Back on track, +5 min and End session (a side task's time is up and no +5 is being asked).</summary>
    public bool ShowsOverButtons => Kind == AwayCardKind.SideTaskOver && !IsAddingFive;

    public bool ShowsAddFive => Kind == AwayCardKind.SideTaskOver && IsAddingFive;

    /// <summary>True once "+5 min" was pressed: the reason box and the wait show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOverButtons), nameof(ShowsAddFive))]
    public partial bool IsAddingFive { get; set; }

    /// <summary>Why the side task needs 5 more minutes. Sealed in the activity log, never sent.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; }

    /// <summary>"Take a breath. You can add 5 minutes in 24 s.", "Write at least 10 characters about why." or "You can add 5 minutes now."</summary>
    [ObservableProperty]
    public partial string AddFiveStatus { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmAddFiveCommand))]
    public partial bool CanAddFive { get; set; }

    /// <summary>Keeps the growing time and the +5 min wait current. Runs every second.</summary>
    /// <param name="awayText">The growing "Away · 4:12" or "Side task · 6:12"; ignored by the length question.</param>
    public void Refresh(DateTimeOffset now, string awayText)
    {
        if (Kind != AwayCardKind.SideTaskLength)
        {
            Detail = awayText;
        }

        if (IsAddingFive && _policy is not null)
        {
            BypassCheckResult result = _policy.Check(_addFiveAskedAt, now, Reason);
            TimeSpan remaining = _policy.RemainingWait(_addFiveAskedAt, now);
            AddFiveStatus = result switch
            {
                BypassCheckResult.WaitNotElapsed => $"Take a breath. You can add 5 minutes in {FormatRemaining(remaining)}.",
                BypassCheckResult.ReasonTooShort => $"Write at least {_minReasonLength} characters about why.",
                _ => "You can add 5 minutes now.",
            };
            CanAddFive = result == BypassCheckResult.Allowed;
        }
    }

    partial void OnReasonChanged(string value)
    {
        Refresh(_time.GetUtcNow(), Detail);
    }

    [RelayCommand]
    private void StartSideTask(string minutes)
    {
        if (int.TryParse(minutes, out int length))
        {
            _actions.StartSideTask(length);
        }
    }

    [RelayCommand]
    private void BackOnTrack()
    {
        _actions.BackOnTrack();
    }

    [RelayCommand]
    private void StillAway()
    {
        _actions.StillAway();
    }

    [RelayCommand]
    private void Close()
    {
        _actions.Close();
    }

    [RelayCommand]
    private void EndSession()
    {
        _actions.EndSession();
    }

    /// <summary>"+5 min": starts the wait (none for the first one) and asks for the reason.</summary>
    [RelayCommand]
    private void AskAddFive()
    {
        BypassSettings rules = _actions.AddFiveRules();
        _policy = new BypassPolicy(rules);
        _minReasonLength = rules.MinReasonLength;
        _addFiveAskedAt = _time.GetUtcNow();
        Reason = string.Empty;
        IsAddingFive = true;
        Refresh(_addFiveAskedAt, Detail);
    }

    [RelayCommand]
    private void CancelAddFive()
    {
        IsAddingFive = false;
        CanAddFive = false;
    }

    [RelayCommand(CanExecute = nameof(CanAddFive))]
    private void ConfirmAddFive()
    {
        _actions.AddFive(Reason.Trim());
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return seconds < 60 ? $"{seconds} s" : $"{seconds / 60}:{seconds % 60:00}";
    }
}
