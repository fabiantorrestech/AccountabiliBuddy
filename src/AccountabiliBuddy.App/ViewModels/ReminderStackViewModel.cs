using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The task reminder cards (ADR 76, spec 03 §D, P7/P8/P12): the toast stack at the bottom right (front card, up to 3 tilted cards
/// behind, "All 6 | ‹ 1/6 ›", the list view, the Snooze menu above the card) and the dimmed full-screen card (Can't miss, or every
/// reminder with "How reminders show" on Always full screen). The ringer (<see cref="IReminderStackHost"/>) fills it from the trusted
/// clock and does what the buttons ask. Holds task titles: shown, never logged. Round 2 R5.5: a goal reminder (another reminder source)
/// shows in the same card with the goal's icon chip, name, reason and progress line, its quick log as the accent button and "Open goal";
/// goal names and reasons are private text too.
/// </summary>
public sealed partial class ReminderStackViewModel : ObservableObject
{
    public const string NudgeText = "Can't do it right now? Put it in Queued and it's part of today's plan.";

    private readonly IReminderStackHost _host;

    public ReminderStackViewModel(IReminderStackHost host, Func<DateTimeOffset> now)
    {
        _host = host;
        Now = now;
        Head = Title = Meta = PrimaryText = CountText = PositionText = ListHead = DismissText = string.Empty;
        ClockDigits = ClockSuffix = FullHead = FullMeta = VolumeNote = string.Empty;
        Reason = OpenText = string.Empty;
        Dot = Brushes.Transparent;
        SourceIconBrush = Brushes.Transparent;
        Rows = [];
        SnoozeMinutes = [];
        SnoozeShortcuts = [];
    }

    /// <summary>The trusted clock, for timing a Can't miss hold.</summary>
    public Func<DateTimeOffset> Now { get; }

    // ---- the stack ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCardView))]
    [NotifyPropertyChangedFor(nameof(ShowsBehindNear), nameof(ShowsBehindMid), nameof(ShowsBehindFar))]
    public partial bool IsListView { get; set; }

    public bool IsCardView => !IsListView;

    /// <summary>"Reminder · at start time · 5 more".</summary>
    [ObservableProperty]
    public partial string Head { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>"AI · 50 min · 150".</summary>
    [ObservableProperty]
    public partial string Meta { get; set; }

    [ObservableProperty]
    public partial IBrush Dot { get; set; }

    [ObservableProperty]
    public partial bool HasNudge { get; set; }

    // ---- another source's card (a goal reminder) ----

    /// <summary>The shown reminder is a goal's (another reminder source): icon chip, reason, Open goal; the label dot hides.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTaskCard))]
    public partial bool IsSourceCard { get; set; }

    public bool IsTaskCard => !IsSourceCard;

    /// <summary>The goal's icon.</summary>
    [ObservableProperty]
    public partial MaterialIconKind SourceIcon { get; set; } = MaterialIconKind.TrackChanges;

    [ObservableProperty]
    public partial IBrush SourceIconBrush { get; set; }

    /// <summary>The reminder's reason, "if not done yet" (private text); empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReason))]
    public partial string Reason { get; set; }

    public bool HasReason => Reason.Length > 0;

    /// <summary>"Open goal".</summary>
    [ObservableProperty]
    public partial string OpenText { get; set; }

    /// <summary>"Start session", "Work on it now" or "Add to Queued".</summary>
    [ObservableProperty]
    public partial string PrimaryText { get; set; }

    [ObservableProperty]
    public partial MaterialIconKind PrimaryIcon { get; set; } = MaterialIconKind.Play;

    /// <summary>More than one reminder: "All 6 | ‹ 1/6 ›" shows.</summary>
    [ObservableProperty]
    public partial bool IsMulti { get; set; }

    /// <summary>"All 6".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>"1/6".</summary>
    [ObservableProperty]
    public partial string PositionText { get; set; }

    /// <summary>"6 reminders · click one to open it".</summary>
    [ObservableProperty]
    public partial string ListHead { get; set; }

    /// <summary>How many tilted cards show behind the front one (0 to 3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBehindNear), nameof(ShowsBehindMid), nameof(ShowsBehindFar))]
    public partial int Behind { get; set; }

    public bool ShowsBehindNear => IsCardView && Behind >= 1;

    public bool ShowsBehindMid => IsCardView && Behind >= 2;

    public bool ShowsBehindFar => IsCardView && Behind >= 3;

    [ObservableProperty]
    public partial IReadOnlyList<ReminderListRowViewModel> Rows { get; set; }

    // ---- snooze ----

    [ObservableProperty]
    public partial bool IsSnoozeOpen { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReminderSnoozeChoiceViewModel> SnoozeMinutes { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ReminderSnoozeChoiceViewModel> SnoozeShortcuts { get; set; }

    // ---- dismiss ----

    /// <summary>"Dismiss in 5 s", "Hold to dismiss" or "Dismiss".</summary>
    [ObservableProperty]
    public partial string DismissText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    public partial bool CanDismiss { get; set; }

    /// <summary>Not busy writing an action: Start, Snooze and the others work.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(SnoozeFiveCommand), nameof(PickTimeCommand), nameof(OpenItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleSnoozeCommand), nameof(SnoozeAllCommand), nameof(DismissAllCommand))]
    public partial bool CanAct { get; set; } = true;

    /// <summary>"Dismiss all" (not Can't miss ones) works once the shown card's dismiss wait is over.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissAllCommand))]
    public partial bool CanDismissAll { get; set; }

    /// <summary>How much of dismiss protection is left (the fill).</summary>
    [ObservableProperty]
    public partial TimeSpan WaitLeft { get; set; }

    [ObservableProperty]
    public partial TimeSpan WaitTotal { get; set; }

    /// <summary>The shown reminder's key (never shown): a Dismiss hold ends when another reminder takes its place.</summary>
    [ObservableProperty]
    public partial string? ShownKey { get; set; }

    /// <summary>Can't miss: Dismiss must be held this long once the wait is over.</summary>
    [ObservableProperty]
    public partial TimeSpan HoldTime { get; set; }

    // ---- the full-screen card ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlainFullScreen))]
    public partial bool IsCantMiss { get; set; }

    /// <summary>A normal reminder shown full screen ("Always full screen"): the Snooze menu instead of "Snooze 5".</summary>
    public bool IsPlainFullScreen => !IsCantMiss;

    /// <summary>"Can't miss · 10 min before" or "Reminder · at start time · 2 more".</summary>
    [ObservableProperty]
    public partial string FullHead { get; set; }

    /// <summary>The big clock's digits, "5:30" (12-hour).</summary>
    [ObservableProperty]
    public partial string ClockDigits { get; set; }

    /// <summary>"PM", small beside the digits.</summary>
    [ObservableProperty]
    public partial string ClockSuffix { get; set; }

    /// <summary>"SDA · due 5:40 PM · 144".</summary>
    [ObservableProperty]
    public partial string FullMeta { get; set; }

    /// <summary>"Couldn't raise the volume" when "Ring even when the PC is muted" was refused; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVolumeNote))]
    public partial string VolumeNote { get; set; }

    public bool HasVolumeNote => VolumeNote.Length > 0;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Primary()
    {
        IsSnoozeOpen = false;
        _host.Primary();
    }

    /// <summary>A goal reminder's "Open goal".</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void OpenItem()
    {
        IsSnoozeOpen = false;
        _host.OpenItem();
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void ToggleSnooze()
    {
        IsSnoozeOpen = !IsSnoozeOpen;
    }

    /// <summary>The Can't miss card's "Snooze 5".</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void SnoozeFive()
    {
        SnoozeFor(5);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void PickTime()
    {
        IsSnoozeOpen = false;
        _host.PickTime();
    }

    /// <summary>
    /// Dismiss: a click, or a full hold (the button then passes <see cref="Controls.ReminderDismissButton.HoldCompleted"/>); the
    /// ringer refuses a Can't miss that wasn't held.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDismissWith))]
    private void Dismiss(object? parameter)
    {
        IsSnoozeOpen = false;
        _host.Dismiss(Equals(parameter, Controls.ReminderDismissButton.HoldCompleted));
    }

    [RelayCommand]
    private void Previous()
    {
        IsSnoozeOpen = false;
        _host.Move(-1);
    }

    [RelayCommand]
    private void Next()
    {
        IsSnoozeOpen = false;
        _host.Move(1);
    }

    [RelayCommand]
    private void ShowAll()
    {
        IsSnoozeOpen = false;
        _host.ShowList(true);
    }

    [RelayCommand]
    private void Back()
    {
        _host.ShowList(false);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void SnoozeAll()
    {
        _host.SnoozeAll();
    }

    [RelayCommand(CanExecute = nameof(CanDismissAllNow))]
    private void DismissAll()
    {
        _host.DismissAll();
    }

    /// <summary>Closes the Snooze menu (Esc, or a click elsewhere on the card).</summary>
    public void CloseSnooze()
    {
        IsSnoozeOpen = false;
    }

    /// <summary>A Snooze menu entry for a length: "10 min".</summary>
    public ReminderSnoozeChoiceViewModel MinutesChoice(int minutes)
    {
        return new ReminderSnoozeChoiceViewModel($"{minutes} min", string.Empty, true, new RelayCommand(() => SnoozeFor(minutes)));
    }

    /// <summary>A Snooze menu entry for one of the three shortcuts; off when its moment has passed (Tonight 6:00 PM at 7 PM).</summary>
    public ReminderSnoozeChoiceViewModel ShortcutChoice(int index, string text, bool isEnabled)
    {
        return new ReminderSnoozeChoiceViewModel(text, string.Empty, isEnabled, new RelayCommand(() => SnoozeAt(index)));
    }

    /// <summary>A list row's command: shows that reminder's card.</summary>
    public RelayCommand OpenCommandFor(int index)
    {
        return new RelayCommand(() => _host.Open(index));
    }

    private bool CanDismissAllNow() => CanAct && CanDismissAll;

    private bool CanDismissWith(object? parameter) => CanDismiss;

    private void SnoozeFor(int minutes)
    {
        if (!CanAct)
        {
            return;
        }

        IsSnoozeOpen = false;
        _host.SnoozeFor(minutes);
    }

    private void SnoozeAt(int index)
    {
        if (!CanAct)
        {
            return;
        }

        IsSnoozeOpen = false;
        _host.SnoozeShortcut(index);
    }
}
