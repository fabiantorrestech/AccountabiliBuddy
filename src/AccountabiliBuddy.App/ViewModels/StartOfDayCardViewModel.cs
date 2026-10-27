using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The start-of-day assistant's card (in front of everything, like the start-nudge card): 1) how many hours of work the
/// user hopes to get done today, 2) what's first (up to 3 tasks), 3) Start. "Not now" takes a reason of at least one word
/// on every step.
/// </summary>
public sealed partial class StartOfDayCardViewModel : ObservableObject, IDisposable
{
    private readonly decimal _initialHours;
    private readonly Func<decimal, Task<string?>> _setHours;
    private readonly Func<int> _openTaskCount;
    private readonly Func<StartOfDayCardViewModel, Task<bool>> _start;
    private readonly Action<StartOfDayCardViewModel, string> _notNow;
    private readonly int _rememberedMinutes;

    /// <param name="setHours">Applies the hours to today's goal (and Budget); returns what is waiting and why, or null.</param>
    /// <param name="openTaskCount">How many open tasks the next session has now.</param>
    /// <param name="onStart">Starts the session; false when it did not start (the card stays).</param>
    /// <param name="comesBack">False under "Only once": then the card doesn't say it comes back.</param>
    public StartOfDayCardViewModel(
        decimal hours, ChecklistViewModel checklist, int rememberedMinutes, bool comesBack,
        Func<decimal, Task<string?>> setHours, Func<int> openTaskCount,
        Func<StartOfDayCardViewModel, Task<bool>> onStart, Action<StartOfDayCardViewModel, string> onNotNow)
    {
        _initialHours = hours;
        Hours = hours;
        Checklist = checklist;
        _rememberedMinutes = rememberedMinutes;
        _setHours = setHours;
        _openTaskCount = openTaskCount;
        _start = onStart;
        _notNow = onNotNow;
        ComesBackText = comesBack ? "Not now asks again later, until a session starts." : string.Empty;
        Reason = string.Empty;
        Note = string.Empty;
        Message = string.Empty;
    }

    /// <summary>Today's goal, and on a Budget day today's budget, in hours (half-hour steps).</summary>
    [ObservableProperty]
    public partial decimal? Hours { get; set; }

    /// <summary>Tomorrow's carried-over tasks and the ones added here: the next session's checklist.</summary>
    public ChecklistViewModel Checklist { get; }

    public string ComesBackText { get; }

    /// <summary>1 = hours, 2 = first tasks, 3 = start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHoursStep), nameof(IsTasksStep), nameof(IsStartStep), nameof(Heading), nameof(StepText), nameof(CanGoBack), nameof(PrimaryText))]
    public partial int Step { get; set; } = 1;

    public bool IsHoursStep => Step == 1;

    public bool IsTasksStep => Step == 2;

    public bool IsStartStep => Step == 3;

    public bool CanGoBack => Step > 1 && !IsBusy;

    public string StepText => $"Start of your day · step {Step} of 3";

    public string Heading => Step switch
    {
        1 => "How many hours of work do you hope to get done today?",
        2 => "What's first?",
        _ => "Ready to start?",
    };

    public string PrimaryText => Step == 3 ? "Start" : "Next";

    /// <summary>What the Start step says will happen.</summary>
    public string StartSummary
    {
        get
        {
            int tasks = _openTaskCount();
            string taskText = tasks == 0 ? "no tasks yet" : tasks == 1 ? "1 task" : $"{tasks} tasks";
            string length = _rememberedMinutes % 60 == 0 ? $"{_rememberedMinutes / 60}h" : _rememberedMinutes < 60 ? $"{_rememberedMinutes} min" : $"{_rememberedMinutes / 60}h {_rememberedMinutes % 60}m";
            return $"{taskText}, {length} counting down. You plan the session first.";
        }
    }

    /// <summary>What is waiting from step 1 (a lower goal or budget), shown on every later step; empty when all applied.</summary>
    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>Why the session did not start; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>Why not now; sealed on this computer, kept 14 days, never sent.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NotNowCommand))]
    public partial string Reason { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    public partial bool IsBusy { get; set; }

    public void Dispose()
    {
        Checklist.Dispose();
    }

    /// <summary>At least one word: a letter or a digit somewhere in it.</summary>
    private bool HasReason() => Reason.Any(char.IsLetterOrDigit);

    private bool CanPrimary() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPrimary))]
    private async Task PrimaryAsync()
    {
        IsBusy = true;
        try
        {
            switch (Step)
            {
                case 1:
                    // Left as it opened, the stepper changes nothing (a goal of 45 minutes shows as 1h but stays 45).
                    if (Hours is decimal hours && hours != _initialHours)
                    {
                        Note = await _setHours(hours) ?? string.Empty;
                    }

                    Step = 2;
                    break;
                case 2:
                    OnPropertyChanged(nameof(StartSummary));
                    Step = 3;
                    break;
                default:
                    Message = string.Empty;
                    if (!await _start(this))
                    {
                        Message = "The session did not start. Try again, or start it from Today.";
                    }

                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 1)
        {
            Step--;
            Message = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(HasReason))]
    private void NotNow()
    {
        _notNow(this, Reason.Trim());
    }
}
