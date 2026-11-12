using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "This session is full" (Tasks redesign `05` §2, spec 03 §B): the incoming task (or step) in its own highlighted card, then "Move one
/// to Queued" with one row per place and a round ⇄ button, and a quiet "Add anyway" at the bottom. A swap sends that place's task to
/// Queued (a step only leaves focus while its task keeps another) and the newcomer takes its number. "Add anyway" asks the over-limit
/// reason through the existing flow (<see cref="PendingMoveViewModel"/>: 10+ characters, kept on this PC, counted for the review) and
/// the add is recorded as over the limit, as always. Several incoming ones are offered one after the other ("Each swap makes room for
/// one"); closing leaves the rest where they were. Nothing here ends, shortens or unlocks anything; titles are shown only.
/// </summary>
public sealed partial class SessionFullViewModel : ObservableObject
{
    private readonly IReadOnlyList<SessionIncomingItem> _incoming;
    private readonly Func<SessionIncomingItem, IReadOnlyList<SessionSlotChoice>> _slotsFor;
    private readonly Func<SessionIncomingItem, SessionSlotChoice, Task<string?>> _swap;
    private readonly Func<SessionIncomingItem, string, Task<AddToTodayResult>> _addAnyway;
    private readonly Func<SessionIncomingItem, bool> _stillWaiting;
    private int _at;

    /// <param name="incoming">What wants a place, in order.</param>
    /// <param name="slotsFor">This session's places whose swap would let that one in, as they are now (none: only Add anyway).</param>
    /// <param name="swap">Frees the place and brings the incoming one in; null when done, else why not (nothing moved; the rows are read again).</param>
    /// <param name="addAnyway">Adds it over the limit with the reason (the session add's own rules); its result as the reason card reads it.</param>
    /// <param name="stillWaiting">Whether it still needs a place (it may have been moved or finished meanwhile).</param>
    public SessionFullViewModel(
        IReadOnlyList<SessionIncomingItem> incoming,
        Func<SessionIncomingItem, IReadOnlyList<SessionSlotChoice>> slotsFor,
        Func<SessionIncomingItem, SessionSlotChoice, Task<string?>> swap,
        Func<SessionIncomingItem, string, Task<AddToTodayResult>> addAnyway,
        Func<SessionIncomingItem, bool> stillWaiting)
    {
        _incoming = incoming;
        _slotsFor = slotsFor;
        _swap = swap;
        _addAnyway = addAnyway;
        _stillWaiting = stillWaiting;
        Sub = IncomingTitle = IncomingDetail = Note = string.Empty;
        IncomingDot = Brushes.Transparent;
        Show();
    }

    /// <summary>Asks the dialog to close (done, or nothing left to offer).</summary>
    public event EventHandler? CloseRequested;

    public string Heading => "This session is full";

    /// <summary>"Swap one out to make room for:" (with "Each swap makes room for one." when several wait).</summary>
    [ObservableProperty]
    public partial string Sub { get; set; }

    [ObservableProperty]
    public partial string IncomingTitle { get; set; }

    /// <summary>"Label · 25 min · from Today · Queued".</summary>
    [ObservableProperty]
    public partial string IncomingDetail { get; set; }

    [ObservableProperty]
    public partial IBrush IncomingDot { get; set; }

    [ObservableProperty]
    public partial bool HasIncomingDot { get; set; }

    /// <summary>"Step" or "Task", above the incoming title.</summary>
    [ObservableProperty]
    public partial string IncomingKind { get; set; } = "Task";

    /// <summary>"2 of 3" while several wait; empty for one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    public partial string ProgressText { get; set; } = string.Empty;

    public bool HasProgress => ProgressText.Length > 0;

    public ObservableCollection<SessionFullSlotViewModel> Slots { get; } = new();

    /// <summary>Why the last swap didn't happen ("That one already left this session.", "Still no room …").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; set; }

    public bool HasNote => Note.Length > 0;

    /// <summary>The reason step of "Add anyway" (the existing over-limit card); null while the swap list shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAskingReason), nameof(IsChoosing))]
    public partial PendingMoveViewModel? Reason { get; set; }

    /// <summary>
    /// The window the dialog shows over instead of the page's (a ringing reminder's topmost window, so the dialog isn't hidden under
    /// it); null for the page's own window.
    /// </summary>
    public Avalonia.Visual? Over { get; init; }

    public bool IsAskingReason => Reason is not null;

    public bool IsChoosing => Reason is null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAnywayCommand))]
    public partial bool IsBusy { get; set; }

    private SessionIncomingItem? Current => _at < _incoming.Count ? _incoming[_at] : null;

    /// <summary>Nothing (still) waits for a place: the dialog isn't shown.</summary>
    public bool IsEmpty => Current is null;

    /// <summary>"Add anyway": the reason card (the same words and rules as everywhere else).</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private void AddAnyway()
    {
        if (Current is not SessionIncomingItem item)
        {
            return;
        }

        bool added = false;
        Reason = new PendingMoveViewModel(
            $"Add “{item.Title}” anyway? This session goes over its limit.",
            canSwap: false,
            [],
            async reason =>
            {
                AddToTodayResult result = await _addAnyway(item, reason);
                added = result == AddToTodayResult.Added;
                return result;
            },
            _ => Task.CompletedTask,
            () =>
            {
                Reason = null;
                if (added)
                {
                    Next();
                }
            });
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAct() => !IsBusy;

    private async Task SwapAsync(SessionSlotChoice choice)
    {
        if (IsBusy || Current is not SessionIncomingItem item)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (await _swap(item, choice) is not string problem)
            {
                Next();
                return;
            }

            Show();
            Note = problem;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // The next one still waiting, or close.
    private void Next()
    {
        _at++;
        Note = string.Empty;
        Show();
    }

    private void Show()
    {
        while (Current is SessionIncomingItem waiting && !_stillWaiting(waiting))
        {
            _at++;
        }

        if (Current is not SessionIncomingItem item)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        int left = _incoming.Count - _at;
        Sub = (left > 1 ? "Each swap makes room for one. " : string.Empty) + "Swap one out to make room for:";
        ProgressText = _incoming.Count > 1 ? $"{_at + 1} of {_incoming.Count}" : string.Empty;
        IncomingKind = item.StepId is null ? "Task" : "Step";
        IncomingTitle = item.Title;
        IncomingDetail = item.Detail;
        IncomingDot = TaskCategoryBrushes.Of(item.LabelColor);
        HasIncomingDot = item.LabelColor is not null;
        Slots.Clear();
        foreach (SessionSlotChoice choice in _slotsFor(item))
        {
            SessionSlotChoice chosen = choice;
            Slots.Add(new SessionFullSlotViewModel(chosen, new AsyncRelayCommand(() => SwapAsync(chosen))));
        }

        HasNoSwap = Slots.Count == 0;
    }

    /// <summary>No single swap makes room for this one (it needs more places than one frees): only Add anyway.</summary>
    [ObservableProperty]
    public partial bool HasNoSwap { get; set; }
}
