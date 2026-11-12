using System.Globalization;
using AccountabiliBuddy.App.Services.Reminders;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Material.Icons;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Rings the task reminders that are due (ADR 76, spec 03 §D, spec 05 §7): the toast stack at the bottom right of the primary screen
/// (front card, tilted cards behind, "All 6 | ‹ 1/6 ›", the list view, the Snooze menu) or, for Can't miss and for every reminder when
/// "How reminders show" is Always full screen, the dimmed full screen with its centred card (<see cref="ReminderStackViewModel"/>). The
/// bell repeats while one shows, rising over 30 s when set, and the output is raised when "Ring even when the PC is muted" is on
/// (always put back, see <see cref="MasterVolumeGuard"/>). The cards follow <see cref="TaskReminderService.Pending"/>: a reminder
/// handled on another device leaves them, and they close when none is left; closed by Alt+F4 while reminders wait, they come back.
/// Only the front reminder rings: its volume ramp and ring time count from the moment it reached the front, and when its ring time is
/// used up it is not rung again. Dismiss waits out dismiss protection from the moment its card came on screen (‹ › restart it), and a
/// Can't miss is dismissed only by a hold; Snooze and Start work at once; while an action is written the buttons are off.
/// During a focus session the service holds normal reminders (setting "During a focus session": wait until it ends, or remind
/// quietly). A quiet one shows as a toast and its bell sounds once; Can't miss rings as it always does. No card takes the focus from
/// the user's work. Nothing here ends, pauses or changes a session: "Start session" puts the task in This session (cap-checked; a full
/// one opens "This session is full") and opens the app's own start controls; during a session it reads "Work on it now".
/// Round 2 R5.5: reminders of another source (<see cref="IReminderSource"/>, goal reminders) come after the task reminders in the same
/// stack with their source's card (icon, name, reason, progress line; its accent button and "Open goal"); they are never full screen and
/// ring quietly when their source says so (ProductaBuddy: one bell, no volume raise); dismiss protection applies to them as to any
/// normal reminder. Use from the UI thread only.
/// </summary>
public sealed class TaskReminderRinger : IDisposable, IReminderStackHost
{
    private const int SnoozeAllMinutes = 10;

    private readonly TaskReminderService _reminders;
    private readonly TaskReminderSettingsService _settings;
    private readonly SessionCueService _cues;
    private readonly MasterVolumeGuard _guard;
    private readonly Func<bool> _sessionRunning;
    private readonly Action _glow;
    private readonly SnoozePickerService _picker;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<CantMissWindow> _fullWindows = [];

    // The full-screen window that shows the card (the primary screen's).
    private CantMissWindow? _cardWindow;
    private ReminderStackViewModel? _viewModel;
    private ReminderStackWindow? _stackWindow;
    private bool _fullScreen;

    // The reminders the cards show, as last laid out, and which one shows (the stack's ‹ ›; always the front one full screen).
    private IReadOnlyList<TaskReminderEntry> _shown = [];
    private int _index;
    private bool _listView;
    private HashSet<string> _listKeys = [];
    private string? _shownKey;
    private DateTimeOffset _shownSince;

    // The shown reminder's source card as last laid out (null for a task's): read once a second without working it out again.
    private ReminderSourceCard? _shownCard;

    private TaskReminderEntry? _front;
    private DateTimeOffset _frontSince;
    private DateTimeOffset _lastSound;
    private bool _soundedInSession;

    // The level the output is raised to for the ring now ("Ring even when the PC is muted"); null while not raised.
    private int? _raisedTo;

    // The front reminder whose raise failed: not tried again until another one is at the front.
    private string? _raiseFailedFor;
    private bool _busy;
    private bool _disposed;

    public TaskReminderRinger(
        TaskReminderService reminders, TaskReminderSettingsService settings, SessionCueService cues, MasterVolumeGuard guard,
        Func<bool> sessionRunning, Action glow)
    {
        _reminders = reminders;
        _settings = settings;
        _cues = cues;
        _guard = guard;
        _sessionRunning = sessionRunning;
        _glow = glow;
        _picker = new SnoozePickerService(reminders, settings);
        _tick.Tick += (_, _) => OnTick();
        _reminders.PendingChanged += (_, _) => OnPendingChanged();
    }

    /// <summary>The app's start controls (the quick-session popup, as the title bar's Start): opened after "Start session" put the task in This session.</summary>
    public Action? OpenStart { get; set; }

    public bool IsRinging => _viewModel is not null;

    /// <summary>
    /// The window a dialog asked for by a reminder card shows over ("Pick a time…", "This session is full"): the full screen's card
    /// window, else the toast stack; null when none shows. They are topmost, so a dialog owned by the main window would open under them.
    /// </summary>
    public Visual? DialogOwner => _cardWindow ?? (Visual?)_stackWindow;

    public void Dispose()
    {
        _disposed = true;
        _tick.Stop();
        CloseWindows();
        _viewModel = null;
        _guard.Restore();
    }

    private TaskReminderRingPolicy Policy => _settings.Current.Policy;

    private TaskReminderEntry? Shown => _index >= 0 && _index < _shown.Count ? _shown[_index] : null;

    // ---- IReminderStackHost: what the cards' buttons ask ----

    public void Primary()
    {
        if (Shown is not TaskReminderEntry entry)
        {
            return;
        }

        if (_reminders.SourceOf(entry) is IReminderSource source)
        {
            _ = ActAsync(async shown =>
            {
                await source.PrimaryAsync(shown);
                _reminders.Refresh();
            });
            return;
        }

        if (TaskReminderCard.ShowsNudge(entry, _reminders.TaskOf(entry.TaskId)?.Bin))
        {
            _ = ActAsync(_reminders.QueueAsync);
            return;
        }

        _ = ActAsync(async shown =>
        {
            // With no session running the task waits in This session and the user's own start controls open; during one it joins the
            // session (its cap holds). A session is never started from here.
            bool running = _sessionRunning();
            bool movedIn = await _reminders.StartAsync(shown);
            if (movedIn && !running && !_disposed)
            {
                OpenStart?.Invoke();
            }
        });
    }

    public void OpenItem()
    {
        if (Shown is not TaskReminderEntry entry || _reminders.SourceOf(entry) is not IReminderSource source)
        {
            return;
        }

        _ = ActAsync(async shown =>
        {
            await source.OpenAsync(shown);
            _reminders.Refresh();
        });
    }

    public void SnoozeFor(int minutes)
    {
        _ = ActAsync(entry => _reminders.SnoozeAsync(entry, TaskSnooze.FromNow(_reminders.Now, minutes)));
    }

    public void SnoozeShortcut(int index)
    {
        IReadOnlyList<TaskSnoozeShortcut> shortcuts = _settings.Current.Shortcuts;
        if (index < 0 || index >= shortcuts.Count)
        {
            return;
        }

        if (TaskSnooze.Resolve(shortcuts[index], _reminders.Now, _reminders.Zone) is not DateTimeOffset until)
        {
            // Its moment passed while the menu was open: the menu shows it off now.
            Present();
            return;
        }

        _ = ActAsync(entry => _reminders.SnoozeAsync(entry, until));
    }

    public void PickTime()
    {
        Visual? owner = DialogOwner;
        _ = ActAsync(entry => _picker.ShowAsync(owner, entry));
    }

    public void Dismiss(bool held)
    {
        // The wait first; a Can't miss also needs the full hold, whoever asks (not only its button).
        if (Shown is not TaskReminderEntry entry || DismissState(entry) is { } state && (state.Waiting || (state.Hold > TimeSpan.Zero && !held)))
        {
            return;
        }

        _ = ActAsync(_reminders.DismissAsync);
    }

    public void Move(int delta)
    {
        _index = TaskReminderCard.Step(_index, delta, _shown.Count);
        Present();
    }

    public void ShowList(bool list)
    {
        // "Snooze all" and "Dismiss all" act only on the reminders that were there when the list opened (one coming in meanwhile is
        // seen first).
        _listKeys = list ? _shown.Select(entry => entry.Key).ToHashSet() : [];
        _listView = list;
        Present();
    }

    public void Open(int index)
    {
        _index = Math.Clamp(index, 0, Math.Max(0, _shown.Count - 1));
        _listView = false;
        Present();
    }

    public void SnoozeAll()
    {
        DateTimeOffset until = TaskSnooze.FromNow(_reminders.Now, SnoozeAllMinutes);
        _ = ActOnAllAsync(ListedNow(), entry => _reminders.SnoozeAsync(entry, until));
    }

    public void DismissAll()
    {
        if (Shown is not TaskReminderEntry entry || DismissState(entry).Waiting)
        {
            return;
        }

        _ = ActOnAllAsync(TaskReminderCard.DismissAll(ListedNow()), _reminders.DismissAsync);
    }

    // The list's reminders that were already there when it opened.
    private IReadOnlyList<TaskReminderEntry> ListedNow()
    {
        return _listView ? _shown.Where(entry => _listKeys.Contains(entry.Key)).ToList() : [];
    }

    // ---- following the due list ----

    private void OnPendingChanged()
    {
        IReadOnlyList<TaskReminderEntry> pending = _reminders.Pending;
        if (pending.Count == 0)
        {
            Stop();
            return;
        }

        // The volume ramp and ring time count from the moment the reminder reaches the front; one waiting behind it has not rung yet.
        TaskReminderEntry front = pending[0];
        if (_front?.Key != front.Key)
        {
            _front = front;
            _frontSince = _reminders.Now;
            _lastSound = DateTimeOffset.MinValue;
            if (_viewModel is not null)
            {
                // A Can't miss that reaches the front later needs the louder output too; a quiet one none.
                FollowVolume(front);
            }
        }

        if (_viewModel is null)
        {
            Begin(front);
            return;
        }

        Present();
    }

    private void Begin(TaskReminderEntry front)
    {
        _soundedInSession = false;
        _shown = [];
        _index = 0;
        _listView = false;
        _shownKey = null;
        FollowVolume(front);
        _glow();
        _viewModel = new ReminderStackViewModel(this, () => _reminders.Now);
        Present();
        _tick.Start();
        OnTick();
    }

    // Lays the cards out for the due list now: which one shows, full screen or the stack, every text and button.
    private void Present()
    {
        IReadOnlyList<TaskReminderEntry> pending = _reminders.Pending;
        if (_viewModel is null || pending.Count == 0)
        {
            return;
        }

        _index = TaskReminderCard.KeepIndex(_shown.Select(entry => entry.Key).ToList(), _index, pending.Select(entry => entry.Key).ToList());
        _shown = pending;
        _fullScreen = ShowsFullScreen(pending[0]);
        if (_fullScreen)
        {
            _index = 0;
        }

        if (_fullScreen || pending.Count < 2)
        {
            _listView = false;
            _listKeys = [];
        }

        TaskReminderEntry shown = pending[_index];
        if (_shownKey != shown.Key)
        {
            // Dismiss protection counts from the moment this card came on screen.
            _shownKey = shown.Key;
            _shownSince = _reminders.Now;
            _viewModel.CloseSnooze();
            _viewModel.ShownKey = shown.Key;
        }

        Fill(_viewModel, pending, shown);
        UpdateButtons();
        ShowWindows();
    }

    private void Fill(ReminderStackViewModel view, IReadOnlyList<TaskReminderEntry> pending, TaskReminderEntry shown)
    {
        DateTimeOffset now = _reminders.Now;
        TimeZoneInfo zone = _reminders.Zone;
        ReminderSourceCard? sourceCard = _reminders.SourceOf(shown)?.Card(shown);
        _shownCard = sourceCard;
        TaskItem? task = _reminders.TaskOf(shown.TaskId);
        TaskLabel? label = _reminders.LabelOf(task?.LabelId);
        int more = pending.Count - 1;
        bool nudge = TaskReminderCard.ShowsNudge(shown, task?.Bin);
        string? number = task?.Number is TaskNumber taskNumber ? taskNumber.Value.ToString(CultureInfo.InvariantCulture) : null;
        string? limit = task?.EstimateMinutes is int minutes ? EstimateText.Of(minutes) : null;
        string? firstRang = nudge ? TaskReminderCard.FirstRang(shown, now, zone) : null;

        view.IsListView = _listView;
        view.Head = TaskReminderCard.Head(shown, zone, more);
        view.Title = task?.Title ?? string.Empty;
        view.Meta = TaskReminderCard.Meta(label?.Name, limit, number, firstRang);
        view.Dot = DotOf(label);
        view.HasNudge = nudge;
        view.IsMulti = pending.Count > 1;
        view.CountText = $"All {pending.Count}";
        view.PositionText = TaskReminderCard.Position(_index, pending.Count);
        view.ListHead = $"{pending.Count} reminders · click one to open it";
        view.Behind = TaskReminderCard.Behind(more);
        view.Rows = pending.Select((entry, i) =>
        {
            if (_reminders.SourceOf(entry)?.Card(entry) is ReminderSourceCard rowCard)
            {
                return new ReminderListRowViewModel(rowCard.Title, rowCard.IconBrush, TaskReminderCard.ListTime(entry.RingAt, now, zone), i == _index, view.OpenCommandFor(i));
            }

            TaskItem? rowTask = _reminders.TaskOf(entry.TaskId);
            return new ReminderListRowViewModel(
                rowTask?.Title ?? string.Empty, DotOf(_reminders.LabelOf(rowTask?.LabelId)), TaskReminderCard.ListTime(entry.RingAt, now, zone),
                i == _index, view.OpenCommandFor(i));
        }).ToList();
        view.SnoozeMinutes = TaskSnooze.Minutes.Select(view.MinutesChoice).ToList();
        view.SnoozeShortcuts = _settings.Current.Shortcuts
            .Select((shortcut, i) => view.ShortcutChoice(i, TaskSnooze.Label(shortcut), TaskSnooze.Resolve(shortcut, now, zone) is not null))
            .ToList();

        view.IsCantMiss = shown.CantMiss;
        view.FullHead = view.Head;
        (view.ClockDigits, view.ClockSuffix) = TaskReminderCard.Clock(shown.Due, zone);
        view.FullMeta = TaskReminderCard.Meta(label?.Name, TaskReminderCard.DueText(task?.DueOn, task?.DueAt, now, zone), number, firstRang);
        FillSource(view, sourceCard, more);
    }

    // Another source's card (a goal reminder): its icon, name, reason and progress line over the task card's fields; never the nudge.
    private static void FillSource(ReminderStackViewModel view, ReminderSourceCard? card, int more)
    {
        view.IsSourceCard = card is not null;
        if (card is null)
        {
            view.Reason = string.Empty;
            view.OpenText = string.Empty;
            return;
        }

        view.Head = more > 0 ? $"{card.Head} · {more} more" : card.Head;
        view.FullHead = view.Head;
        view.Title = card.Title;
        view.Meta = card.Meta;
        view.FullMeta = card.Meta;
        view.Reason = card.Reason;
        view.SourceIcon = card.Icon;
        view.SourceIconBrush = card.IconBrush;
        view.Dot = card.IconBrush;
        view.OpenText = card.OpenText;
        view.HasNudge = false;
    }

    private void OnTick()
    {
        IReadOnlyList<TaskReminderEntry> pending = _reminders.Pending;
        if (pending.Count == 0)
        {
            Stop();
            return;
        }

        DateTimeOffset now = _reminders.Now;
        TaskReminderRingPolicy policy = Policy;
        TaskReminderEntry front = pending[0];
        TimeSpan rung = now - _frontSince;

        // Only the front one rings, so only its ring time runs; when it is used up it is not rung again and the next comes to the front.
        if (policy.Expired(rung, front.CantMiss))
        {
            _reminders.RingTimedOut(front);
            return;
        }

        // A session that started or ended, or "How reminders show" changed, can switch between the toast and full screen.
        if (ShowsFullScreen(front) != _fullScreen)
        {
            Present();
        }

        UpdateButtons();
        FollowVolume(front);
        PlaySoundIfDue(front, now, rung, policy);
        _stackWindow?.Place();
    }

    // "Ring even when the PC is muted": the output follows the front reminder. Raised only while it rings out loud (a normal reminder
    // during a session, one quiet bell, gets none, and the raise is put back when the front turns quiet); a Can't miss raises to 100 and
    // a normal one after it lowers to the normal level, never below the user's own (put back first, then raised from there). A raise
    // that fails is tried again only for the next front. Core Audio is touched only on a change.
    private void FollowVolume(TaskReminderEntry front)
    {
        bool loud = _settings.Current.RingEvenWhenMuted && !IsQuiet(front);
        if (!loud)
        {
            if (_raisedTo is not null)
            {
                _guard.Restore();
                _raisedTo = null;
            }

            return;
        }

        int level = front.CantMiss ? 100 : _settings.Current.Volume;
        if (_raisedTo == level || _raiseFailedFor == front.Key)
        {
            return;
        }

        if (_raisedTo is int higher && level < higher)
        {
            _guard.Restore();
            _raisedTo = null;
        }

        if (_guard.RaiseFor(level))
        {
            _raisedTo = level;
            _raiseFailedFor = null;
        }
        else
        {
            _raiseFailedFor = front.Key;
        }
    }

    private void PlaySoundIfDue(TaskReminderEntry front, DateTimeOffset now, TimeSpan rung, TaskReminderRingPolicy policy)
    {
        bool quiet = IsQuiet(front);
        if (quiet && _soundedInSession)
        {
            return;
        }

        if (now - _lastSound < policy.RepeatEvery(front.CantMiss))
        {
            return;
        }

        _lastSound = now;
        _soundedInSession |= quiet;
        _cues.Play(SoundChoice.BellId, policy.VolumeAt(rung, front.CantMiss));
    }

    // One quiet bell, no volume raise: a normal reminder during a session (as before), or one whose source rings quietly (PB goal reminders).
    private bool IsQuiet(TaskReminderEntry front)
    {
        return (_sessionRunning() && !front.CantMiss) || _reminders.SourceOf(front)?.RingsQuietly(front) == true;
    }

    // A task's reminder follows "How reminders show" and Can't miss; another source's reminder is a suggestion: always the toast.
    private bool ShowsFullScreen(TaskReminderEntry front)
    {
        return front.Origin == TaskReminderOrigin.Task && TaskReminderCard.ShowsFullScreen(front, _settings.Current.Display, _sessionRunning());
    }

    private TaskReminderDismissState DismissState(TaskReminderEntry shown)
    {
        return TaskReminderCard.Dismiss(Policy, _reminders.Now - _shownSince, shown.CantMiss);
    }

    // Once a second and after every change: Dismiss's wait and words, the busy state, the accent button's words, the volume note.
    private void UpdateButtons()
    {
        if (_viewModel is null || Shown is not TaskReminderEntry shown)
        {
            return;
        }

        TaskReminderDismissState state = DismissState(shown);
        _viewModel.DismissText = state.Text;
        _viewModel.WaitLeft = state.Left;
        _viewModel.WaitTotal = Policy.DismissWait();
        _viewModel.HoldTime = state.Hold;
        _viewModel.CanDismiss = !state.Waiting && !_busy;
        _viewModel.CanDismissAll = !state.Waiting;
        _viewModel.CanAct = !_busy;
        bool nudge = _viewModel.HasNudge;
        if (shown.Origin != TaskReminderOrigin.Task && _shownCard is ReminderSourceCard card)
        {
            _viewModel.PrimaryText = card.PrimaryText;
            _viewModel.PrimaryIcon = card.PrimaryIcon;
        }
        else
        {
            _viewModel.PrimaryText = nudge ? "Add to Queued" : _sessionRunning() ? "Work on it now" : "Start session";
            _viewModel.PrimaryIcon = nudge ? MaterialIconKind.PlaylistPlus : MaterialIconKind.Play;
        }
        _viewModel.VolumeNote = _front is not null && _raiseFailedFor == _front.Key ? "Couldn't raise the volume" : string.Empty;
    }

    // One action at a time: while it is written, the buttons are off (a second click would snooze twice or act on the next reminder).
    private async Task ActAsync(Func<TaskReminderEntry, Task> act)
    {
        if (_busy || Shown is not TaskReminderEntry entry)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await act(entry);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async Task ActOnAllAsync(IReadOnlyList<TaskReminderEntry> entries, Func<TaskReminderEntry, Task> act)
    {
        if (_busy || entries.Count == 0)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            foreach (TaskReminderEntry entry in entries.ToList())
            {
                await act(entry);
            }
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    // ---- the windows ----

    private void ShowWindows()
    {
        if (_disposed || _viewModel is null)
        {
            return;
        }

        if (_fullScreen)
        {
            CloseStack();
            if (_fullWindows.Count == 0)
            {
                OpenFullScreen(_viewModel);
            }
        }
        else
        {
            CloseFullScreen();
            if (_stackWindow is null)
            {
                OpenStack(_viewModel);
            }
        }
    }

    private void OpenStack(ReminderStackViewModel view)
    {
        var window = new ReminderStackWindow { DataContext = view };
        _stackWindow = window;
        window.Closed += (_, _) =>
        {
            // Closed by Alt+F4 (not by us): reminders still wait, so it comes back.
            if (ReferenceEquals(_stackWindow, window))
            {
                _stackWindow = null;
                Dispatcher.UIThread.Post(Reopen);
            }
        };
        window.Place();
        window.Show();
    }

    // One dimmed window per screen; the primary screen's (else the first's) shows the card.
    private void OpenFullScreen(ReminderStackViewModel view)
    {
        var first = new CantMissWindow { DataContext = view };
        IReadOnlyList<Screen> screens = first.Screens.All;
        Screen? cardScreen = first.Screens.Primary ?? screens.FirstOrDefault();
        if (screens.Count == 0 || cardScreen is null)
        {
            first.WindowState = WindowState.FullScreen;
            first.ShowCard();
            _cardWindow = first;
            Track(first);
            return;
        }

        foreach (Screen screen in screens)
        {
            CantMissWindow window = _fullWindows.Count == 0 ? first : new CantMissWindow { DataContext = view };
            window.Cover(screen);
            if (_cardWindow is null && (ReferenceEquals(screen, cardScreen) || screen.Bounds == cardScreen.Bounds))
            {
                window.ShowCard();
                _cardWindow = window;
            }

            Track(window);
        }
    }

    private void Track(CantMissWindow window)
    {
        _fullWindows.Add(window);
        window.Closed += (_, _) =>
        {
            // One closed by Alt+F4 (not by us): all come back together.
            if (_fullWindows.Remove(window))
            {
                CloseFullScreen();
                Dispatcher.UIThread.Post(Reopen);
            }
        };
        window.Show();
    }

    private void Reopen()
    {
        if (!_disposed && _viewModel is not null && _reminders.Pending.Count > 0)
        {
            ShowWindows();
        }
    }

    private void Stop()
    {
        _tick.Stop();
        _front = null;
        _shown = [];
        _shownKey = null;
        _shownCard = null;
        CloseWindows();
        _viewModel = null;
        _guard.Restore();
        _raisedTo = null;
        _raiseFailedFor = null;
    }

    private void CloseWindows()
    {
        CloseStack();
        CloseFullScreen();
    }

    private void CloseStack()
    {
        ReminderStackWindow? window = _stackWindow;
        _stackWindow = null;
        window?.Close();
    }

    private void CloseFullScreen()
    {
        List<CantMissWindow> windows = [.. _fullWindows];
        _fullWindows.Clear();
        _cardWindow = null;
        foreach (CantMissWindow window in windows)
        {
            window.Close();
        }
    }

    // A label's dot; "No label" gets the theme's faint colour.
    private static IBrush DotOf(TaskLabel? label)
    {
        if (label is not null)
        {
            return TaskCategoryBrushes.Of(label.Color);
        }

        Application? app = Application.Current;
        return app is not null && app.TryGetResource("Ab.Faint", app.ActualThemeVariant, out object? faint) && faint is IBrush brush
            ? brush
            : Brushes.Gray;
    }
}
