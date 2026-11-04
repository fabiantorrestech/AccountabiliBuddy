using System.Globalization;
using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The shutdown routine (redesign phase 5, design J; step 13): a short stepped flow on Today that ends in "Work over for today".
/// It opens by itself once the day's last work part is over and no session runs (documents key "shutdown": the plan date it wraps
/// up, so work ending after midnight still gets its card; up to 6 hours late), or by hand with "Start shutdown" on any day, with or
/// without work parts. A "work ends soon" reminder comes a settable number of minutes before (15 by default). "Not now" closes the card
/// and brings it back once, 30 minutes later. A card left open closes by itself 6 hours after it opened or when the next work part
/// starts, whichever is first; nothing on it is applied then, except a typed tomorrow's first task (kept under the Tasks page's
/// "Auto-saved") and a written review. Each step writes its choices when "Next" is pressed. Finishing records the day; once the day's work
/// is over (finishing in the middle of the workday changes nothing until then) and until the next work part (or 4:00 AM) Today says
/// "Work over for today" and the work-start nudges stay quiet, never during a work part. Nothing is ever locked, and bedtime and
/// sleep are untouched. The card never opens during a session, and never reads or writes a session's PC-off stretches (ADR 60).
/// Use from the UI thread only.
/// </summary>
public sealed class ShutdownService : IDisposable
{
    private const string DocumentKey = "shutdown";
    private const int LookAheadDays = 14;
    private const int LaterDueWithinDays = 14;
    private const int MaxEventsPerDay = 4;
    private const int MaxDoneTitles = 5;
    private const int PlanRowCount = 3;
    private const int KeptDays = 90;

    // A day's card still comes this long after its work ended (e.g. the app was closed then); later it's skipped, and a
    // card left open closes by itself.
    private static readonly TimeSpan LateLimit = TimeSpan.FromHours(6);

    // "Not now" brings the card back once after this.
    private static readonly TimeSpan NotNowReturnAfter = TimeSpan.FromMinutes(30);

    // "Work over for today" never outlasts the night: a new day starts at 4:00 AM.
    private static readonly TimeSpan NewDayAt = TimeSpan.FromHours(4);

    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly SessionTasksService _tasks;
    private readonly ComebackStore _comebacks;
    private readonly SqliteJsonDocumentStore _store;
    private readonly AutoSavedTaskStore _autoSaved;
    private readonly ShutdownReviewStore _reviews;
    private readonly CalendarService _calendar;
    private ShutdownState _state = new(null);

    // The plan date the open card wraps up, when it opened and when it closes by itself.
    private DateOnly _openFor;
    private DateTimeOffset _openedAt;
    private DateTime _closesAt;

    // What was typed on a card put off with "Not now", kept for the card that comes back (also kept under Auto-saved until then).
    private ParkedDraft? _parked;

    // "Work over" is recomputed every second; its end only changes with the finish or the work end, so it is kept for the tick that asks again.
    private (DateTime Anchor, DateTime Until)? _workOverEnd;

    // One end-of-work reminder per plan date, but the QR reminder waits for the card only this long after it opened.
    private static readonly TimeSpan QrWaitsFor = TimeSpan.FromMinutes(30);

    public ShutdownService(
        SessionController sessions, ConfigurationService configuration, SessionTasksService tasks, ComebackStore comebacks,
        SqliteJsonDocumentStore store, AutoSavedTaskStore autoSaved, ShutdownReviewStore reviews, CalendarService calendar)
    {
        _sessions = sessions;
        _configuration = configuration;
        _tasks = tasks;
        _comebacks = comebacks;
        _store = store;
        _autoSaved = autoSaved;
        _reviews = reviews;
        _calendar = calendar;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>Counted focus on a date, for the day plan's Budget days (a met budget ends the day's work windows). Set by the app.</summary>
    public Func<DateOnly, TimeSpan>? FocusedOn { get; set; }

    /// <summary>The night before a date from the sleep settings, for a Budget day whose plan has no Sleep part. Set by the app.</summary>
    public Func<DateOnly, SleepTimes?>? NightFallback { get; set; }

    /// <summary>The day plan the guards read: the stored plan with calendar events laid over it (ADR 58). Set by the app; null = the stored plan.</summary>
    public Func<DayPlan>? GuardedPlan { get; set; }

    /// <summary>A date's focus against the goal, for the review step (all devices; sessions added by hand included). Set by the app; null = no number.</summary>
    public Func<DateOnly, GoalProgress?>? FocusFor { get; set; }

    /// <summary>Time off track inside a date's sessions, for the review step; null when activity history is off. Set by the app.</summary>
    public Func<DateOnly, Task<TimeSpan?>>? DriftFor { get; set; }

    /// <summary>Opens the Plan page (the planning step's link). Set by the app.</summary>
    public Action? OpenPlan { get; set; }

    /// <summary>A day off (Pre-Work said today is not a work day): no reminder comes, like the start nudge. Set by the app.</summary>
    public Func<bool> IsDayOff { get; set; } = () => false;

    private DayPlan PlanInForce => GuardedPlan?.Invoke() ?? _configuration.Current.DayPlan;

    /// <summary>The routine's settings, held to their limits.</summary>
    public ShutdownSettings Settings => (_state.Settings ?? ShutdownSettings.Default).Normalized();

    /// <summary>The card showing on Today, or null.</summary>
    public ShutdownCardViewModel? Current { get; private set; }

    /// <summary>The user has looked at the routine: its settings were changed or a day was finished (the set-up assistant's "set up?" check).</summary>
    public bool HasBeenConfigured => _state.Settings is not null || (_state.Days?.Count ?? 0) > 0;

    /// <summary>"Work over for today" is in force: the routine was finished and the next work part has not started. Null otherwise.</summary>
    public ShutdownWorkOver? WorkOver { get; private set; }

    /// <summary>True while <see cref="WorkOver"/> holds: the work-start nudges stay quiet.</summary>
    public bool IsWorkOver => WorkOver is not null;

    /// <summary>The card in <see cref="Current"/> came back after a restart, not from the end of work: no second "work's done" notice.</summary>
    public bool CardWasRestored { get; private set; }

    /// <summary>Raised when <see cref="Current"/> opens or closes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when <see cref="WorkOver"/> starts or ends.</summary>
    public event EventHandler? WorkOverChanged;

    /// <summary>Raised when a card that closed by itself kept its tomorrow's first task under the Tasks page's "Auto-saved".</summary>
    public event EventHandler? AutoSaved;

    /// <summary>Raised once a day, the set number of minutes before work ends, with no session running and no card open.</summary>
    public event EventHandler<ShutdownReminder>? ReminderDue;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<ShutdownState>(DocumentKey)).GetAwaiter().GetResult() ?? new ShutdownState(null);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = new ShutdownState(null);
        }

        RefreshWorkOver(LocalNow());
    }

    /// <summary>Saves the settings. Turning the routine off closes a card it opened by itself; a card started by hand stays.</summary>
    public async Task SaveSettingsAsync(ShutdownSettings settings)
    {
        _state = _state with { Settings = settings.Normalized() };
        if (!Settings.Enabled)
        {
            _state = _state with { ReturnAt = null };
            if (Current is not null && !_state.OpenedByHand)
            {
                CloseCard();
            }
        }

        await SaveAsync();
    }

    /// <summary>
    /// "Start shutdown": opens the card now for the current plan date, on any day (also one with no work parts) and also with the routine
    /// switched off. It doesn't open during a session.
    /// </summary>
    /// <returns>True when the card is open; false while a session runs.</returns>
    public bool StartByHand()
    {
        // During a session even an open card is hidden, so there is nothing to show: the caller says it waits.
        if (_sessions.State.IsActive)
        {
            return false;
        }

        if (Current is not null)
        {
            return true;
        }

        Open(PlanDateOf(LocalNow()), byHand: true);
        return true;
    }

    /// <summary>
    /// True while a shutdown card is due to open (it waits for a session to end), or is open and was opened less than 30 minutes ago. The QR
    /// scan reminder holds back meanwhile; a card nobody answers does not hold it for hours.
    /// </summary>
    public bool IsCardOpenOrDue()
    {
        if (Current is not null && _sessions.Now - _openedAt < QrWaitsFor)
        {
            return true;
        }

        return Current is null && Settings.Enabled && DueDate(LocalNow()) is not null;
    }

    /// <summary>Opens the card once the day's work is over, reminds before that, and closes one left open too long. Runs every second.</summary>
    public void CheckNow()
    {
        DateTime now = LocalNow();
        RefreshWorkOver(now);
        // Even while a session runs: the card is only hidden then.
        if (Current is not null && now >= _closesAt)
        {
            AutoClose();
        }

        if (Current is not null || _sessions.State.IsActive)
        {
            return;
        }

        if (RestoreOpenCard(now) || ReopenAfterNotNow(now))
        {
            return;
        }

        if (!Settings.Enabled)
        {
            return;
        }

        if (DueDate(now) is DateOnly due)
        {
            Open(due, byHand: false);
            return;
        }

        RemindIfDue(now);
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    // ---- Plan dates and the work windows ----

    private DateTime LocalNow()
    {
        return TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime;
    }

    private DateTime ToLocal(DateTimeOffset time)
    {
        return TimeZoneInfo.ConvertTime(time, _sessions.TimeProvider.LocalTimeZone).DateTime;
    }

    // The plan date a manual start wraps up: before 4:00 AM it is still the day that went on past midnight.
    private static DateOnly PlanDateOf(DateTime now)
    {
        return DateOnly.FromDateTime(now.Add(-NewDayAt));
    }

    // When the last work part that starts on [date] ends, as of [now]; null on a day without work parts. The caller's [now] is passed through:
    // a met Budget ends at the moment it is met (the window is clipped to that now), so a later clock read would always leave the end ahead.
    private DateTime? WorkEnd(DateOnly date, DateTime now)
    {
        List<PlannedFocus> work = WorkStartingOn(date, now);
        return work.Count == 0 ? null : work.Max(part => part.Window.End);
    }

    // When the first work part that starts after [moment] starts; null when none starts that day or the next.
    private DateTime? NextWorkStartAfter(DateTime moment, DateTime now)
    {
        DateOnly day = DateOnly.FromDateTime(moment);
        return new[] { day, day.AddDays(1) }
            .SelectMany(date => WorkStartingOn(date, now))
            .Select(part => part.Window.Start)
            .Where(start => start > moment)
            .Select(start => (DateTime?)start)
            .Min();
    }

    // The work parts that start on [date] (one may run past midnight).
    private List<PlannedFocus> WorkStartingOn(DateOnly date, DateTime now)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        return DayWorkWindows.WorkWindows(PlanInForce, new LocalWindow(start, start.AddDays(2)), now, FocusedOn, NightFallback)
            .Where(part => DateOnly.FromDateTime(part.Window.Start) == date)
            .ToList();
    }

    // The plan date whose card is due to open by itself: work over, no more than 6 hours ago, not shown, not finished. Yesterday's
    // too: a last work part that ends at or after midnight belongs to the day it started on.
    private DateOnly? DueDate(DateTime now)
    {
        DateOnly today = DateOnly.FromDateTime(now);
        foreach (DateOnly date in new[] { today.AddDays(-1), today })
        {
            if (AlreadyHandled(date) || WorkEnd(date, now) is not DateTime end || now < end || now - end > LateLimit)
            {
                continue;
            }

            return date;
        }

        return null;
    }

    private bool AlreadyHandled(DateOnly date)
    {
        return (_state.LastShownOn is DateOnly shown && shown >= date) || IsFinished(date);
    }

    // A day finished in the middle of the workday doesn't count: the card still comes when work ends.
    private bool IsFinished(DateOnly date)
    {
        return _state.Days?.Any(day => day.Date == date && day.AfterWork) ?? false;
    }

    // When a card opened at [anchor] closes by itself: 6 hours later, or when the next work part starts if that is sooner.
    private DateTime ClosesAtFor(DateTime anchor, DateTime now)
    {
        DateTime lateEnd = anchor + LateLimit;
        return NextWorkStartAfter(anchor, now) is DateTime nextWork && nextWork < lateEnd ? nextWork : lateEnd;
    }

    // ---- Opening, restoring and putting off ----

    private void Open(DateOnly date, bool byHand)
    {
        _openFor = date;
        _openedAt = _sessions.Now;
        DateTime now = LocalNow();
        _closesAt = ClosesAtFor(now, now);
        DateOnly shown = _state.LastShownOn is DateOnly last && last > date ? last : date;
        _state = _state with
        {
            LastShownOn = byHand ? _state.LastShownOn : shown,
            OpenFor = date,
            OpenedAt = _openedAt,
            OpenedByHand = byHand,
            ReturnAt = null,
        };
        _ = SaveAsync();
        CardWasRestored = false;
        ShowCard(BuildCard(date, byHand), date);
    }

    private void ShowCard(ShutdownCardViewModel card, DateOnly date)
    {
        if (_parked is { } parked && parked.Date == date)
        {
            _ = RestoreDraftAsync(card, parked);
        }

        _parked = null;
        Current = card;
        Changed?.Invoke(this, EventArgs.Empty);
        _ = FillDriftAsync(card, date);
    }

    // The card that comes back after "Not now" gets what was typed on the one before, and it leaves Auto-saved again.
    private async Task RestoreDraftAsync(ShutdownCardViewModel card, ParkedDraft parked)
    {
        // All texts go back first (synchronously); only the ones that found their place leave Auto-saved.
        var restored = new List<ParkedText>();
        foreach (ParkedText text in parked.Texts)
        {
            if (text.Row is null)
            {
                card.TomorrowTask = text.Title;
                restored.Add(text);
            }
            else if (text.Row < card.PlanRows.Count)
            {
                card.PlanRows[text.Row.Value].Text = text.Title;
                if (text.PartIndex is int part && part < card.PlanRows[text.Row.Value].Parts.Count)
                {
                    card.PlanRows[text.Row.Value].PartIndex = part;
                }

                restored.Add(text);
            }
        }

        foreach (ParkedText text in restored)
        {
            try
            {
                await _autoSaved.RemoveAsync(text.AutoSavedId);
            }
            catch (SqliteException)
            {
                // Best effort: it stays under Auto-saved as well.
            }
        }
    }

    // The app restarted with a card open: it comes back (as long as it would still be open and the routine, or a hand start, asked for it).
    private bool RestoreOpenCard(DateTime now)
    {
        if (_state.OpenFor is not DateOnly date || _state.OpenedAt is not DateTimeOffset openedAt)
        {
            return false;
        }

        if ((!_state.OpenedByHand && !Settings.Enabled) || now >= ClosesAtFor(ToLocal(openedAt), now))
        {
            _state = _state with { OpenFor = null, OpenedAt = null, OpenedByHand = false, LastShownOn = LastShownAfterClose(date, _state.OpenedByHand, now) };
            _ = SaveAsync();
            return false;
        }

        _openFor = date;
        _openedAt = openedAt;
        _closesAt = ClosesAtFor(ToLocal(openedAt), now);
        CardWasRestored = true;
        ShowCard(BuildCard(date, _state.OpenedByHand), date);
        return true;
    }

    // "Not now" came back after its wait: the card opens once more, when its day is still within the late limit.
    private bool ReopenAfterNotNow(DateTime now)
    {
        if (_state.ReturnAt is not DateTimeOffset returnAt || _state.NotNowOn is not DateOnly date || !Settings.Enabled)
        {
            return false;
        }

        if (_sessions.Now < returnAt)
        {
            return false;
        }

        if (WorkEnd(date, now) is not DateTime end || now - end > LateLimit)
        {
            _state = _state with { ReturnAt = null };
            _ = SaveAsync();
            return false;
        }

        Open(date, byHand: false);
        return true;
    }

    private async Task NotNowAsync()
    {
        ShutdownCardViewModel? card = Current;
        if (card is null)
        {
            return;
        }

        bool returns = !_state.OpenedByHand && _state.NotNowOn != _openFor;
        DateOnly date = _openFor;
        _state = _state with
        {
            NotNowOn = _state.OpenedByHand ? _state.NotNowOn : date,
            ReturnAt = returns ? _sessions.Now + NotNowReturnAfter : null,
        };
        await CloseCardAsync();
        List<ParkedText> kept = await KeepUnsavedAsync(card, date);
        // A card that comes back gets what was typed back; one that doesn't leaves it under Auto-saved.
        _parked = returns && kept.Count > 0 ? new ParkedDraft(date, kept) : null;
    }

    // The card was left open too long: it closes like Not now without a return, but what was typed is kept (see KeepUnsavedAsync).
    private void AutoClose()
    {
        ShutdownCardViewModel card = Current!;
        DateOnly date = _openFor;
        CloseCard();
        _ = KeepAndAnnounceAsync(card, date);
    }

    private async Task KeepAndAnnounceAsync(ShutdownCardViewModel card, DateOnly date)
    {
        if ((await KeepUnsavedAsync(card, date)).Count > 0)
        {
            AutoSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    // What was typed but never applied is not lost: a review (sealed) and the tasks typed for tomorrow (first task and planned rows, under
    // "Auto-saved"). A task chosen from a list is still in its list, so it is not copied. Applied inputs were cleared by their step.
    private async Task<List<ParkedText>> KeepUnsavedAsync(ShutdownCardViewModel card, DateOnly date)
    {
        var kept = new List<ParkedText>();
        try
        {
            if (NeedsReviewSave(card, date))
            {
                await _reviews.SaveReviewAsync(date, card.Review.Feeling, card.Review.Note);
            }

            string tomorrow = card.TomorrowTask.Trim();
            if (tomorrow.Length > 0 && card.TomorrowSource is null && await _autoSaved.AddAsync(tomorrow, date) is { } item)
            {
                kept.Add(new ParkedText(null, null, tomorrow, item.Id));
            }

            for (int row = 0; row < card.PlanRows.Count; row++)
            {
                ShutdownPlanRowViewModel planRow = card.PlanRows[row];
                if (planRow.Title.Length > 0 && planRow.Source is null && await _autoSaved.AddAsync(planRow.Title, date) is { } saved)
                {
                    kept.Add(new ParkedText(row, planRow.PartIndex, planRow.Title, saved.Id));
                }
            }
        }
        catch (SqliteException)
        {
            // Best effort: the item still shows until the app restarts.
        }

        return kept;
    }

    // A review is written when something was answered, or when an earlier answer was cleared.
    private bool NeedsReviewSave(ShutdownCardViewModel card, DateOnly date)
    {
        return card.Review.HasAnswer || _reviews.Of(date) is not null;
    }

    // A card started by hand doesn't come back by itself (hard rules). An automatic card is marked shown when it opens; a hand card is
    // marked when it closes after the work end (Not now, auto-close, a finish, a restart that finds it expired), or the next tick would
    // open it again as an automatic one. Closed before the work end, the evening card still comes.
    private DateOnly? LastShownAfterClose(DateOnly date, bool byHand, DateTime now)
    {
        DateOnly? shown = _state.LastShownOn;
        bool workOver = byHand && WorkEnd(date, now) is DateTime end && end <= now;
        return workOver && (shown is null || shown < date) ? date : shown;
    }

    private void CloseCard()
    {
        _ = CloseCardAsync();
    }

    private Task CloseCardAsync()
    {
        DateOnly? shownOn = LastShownAfterClose(_openFor, _state.OpenedByHand, LocalNow());
        _state = _state with { OpenFor = null, OpenedAt = null, OpenedByHand = false, LastShownOn = shownOn };
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return SaveAsync();
    }

    // ---- The reminder ----

    private void RemindIfDue(DateTime now)
    {
        int minutes = Settings.ReminderMinutes;
        if (minutes <= 0 || IsDayOff())
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(now);
        foreach (DateOnly date in new[] { today.AddDays(-1), today })
        {
            if ((_state.RemindedOn is DateOnly reminded && reminded >= date) || AlreadyHandled(date))
            {
                continue;
            }

            if (WorkEnd(date, now) is not DateTime end || now >= end || now < end.AddMinutes(-minutes))
            {
                continue;
            }

            _state = _state with { RemindedOn = date };
            _ = SaveAsync();
            ReminderDue?.Invoke(this, new ShutdownReminder(date, end));
            return;
        }
    }

    // ---- Work over for today ----

    // Recomputed every second from the last finished day; raised only when it starts or ends. It never starts while the day's work is
    // still ahead (finishing at 9:30 in a 9-5 day changes nothing until 5:00 PM; a Budget day's work ends at bedtime, or when the budget
    // is met), so the work-start nudges keep working through the workday.
    private void RefreshWorkOver(DateTime now)
    {
        ShutdownWorkOver? computed = null;
        if (_state.Days?.MaxBy(day => day.CompletedAt) is ShutdownDay last)
        {
            DateTime finished = ToLocal(last.CompletedAt);
            if (finished <= now && now - finished < TimeSpan.FromHours(30))
            {
                DateTime? workEnd = WorkEnd(last.Date, now);
                if (workEnd is null || workEnd <= now)
                {
                    DateTime until = WorkOverEnd(workEnd is DateTime over && over > finished ? over : finished, now);
                    if (now < until)
                    {
                        computed = new ShutdownWorkOver(last.Line, last.Checked, until);
                    }
                }
            }
        }

        bool same = computed is null
            ? WorkOver is null
            : WorkOver is not null && computed.Until == WorkOver.Until && computed.Line == WorkOver.Line && ReferenceEquals(computed.Checked, WorkOver.Checked);
        if (same)
        {
            return;
        }

        WorkOver = computed;
        WorkOverChanged?.Invoke(this, EventArgs.Empty);
    }

    // The next work part after [anchor] (the finish, or the work end when that came later), or 4:00 AM when that comes first.
    private DateTime WorkOverEnd(DateTime anchor, DateTime now)
    {
        if (_workOverEnd is { } known && known.Anchor == anchor && known.Until > now)
        {
            return known.Until;
        }

        DateTime morning = (anchor.TimeOfDay < NewDayAt ? anchor.Date : anchor.Date.AddDays(1)) + NewDayAt;
        DateTime until = NextWorkStartAfter(anchor, now) is DateTime nextWork && nextWork < morning ? nextWork : morning;
        _workOverEnd = (anchor, until);
        return until;
    }

    // ---- Building the card ----

    private ShutdownCardViewModel BuildCard(DateOnly date, bool byHand)
    {
        ShutdownSettings settings = Settings;
        var shown = new List<ShutdownStep>();
        var empty = new List<string>();
        IReadOnlyList<ShutdownComebackRow> comebacks = settings.Capture ? OpenComebacks() : [];
        IReadOnlyList<ShutdownTaskGroup> unfinished = settings.Unfinished ? UnfinishedOn(date) : [];
        foreach (ShutdownStep step in settings.Steps)
        {
            if (step == ShutdownStep.Capture && comebacks.Count == 0)
            {
                empty.Add("No parked thoughts");
            }
            else if (step == ShutdownStep.Unfinished && unfinished.Count == 0)
            {
                empty.Add("No unfinished tasks");
            }
            else
            {
                shown.Add(step);
            }
        }

        (IReadOnlyList<ShutdownLaterRow> later, IReadOnlyList<ShutdownAheadDay> ahead) = settings.LookAhead ? LookAheadFrom(date) : ([], []);
        IReadOnlyList<ShutdownPlanRowViewModel> planRows = settings.PlanTomorrow ? PlanRowsFor(date) : [];
        ShutdownReviewViewModel review = ReviewFor(date);
        var actions = new ShutdownCardActions(
            (step, card) => ApplyStepAsync(step, card, date), card => FinishAsync(card, date), NotNowAsync, () => OpenPlan?.Invoke());
        return new ShutdownCardViewModel(shown, empty, settings.ClosingLine, byHand, comebacks, unfinished, later, ahead, planRows, review, actions, _tasks);
    }

    private List<ShutdownComebackRow> OpenComebacks()
    {
        return _comebacks.Open.Select(comeback => new ShutdownComebackRow(comeback.Id, comeback.Text)).ToList();
    }

    // The tasks left open in today's sessions that nothing carries yet, under their category.
    private List<ShutdownTaskGroup> UnfinishedOn(DateOnly date)
    {
        List<string> waiting = _tasks.Current.Tasks.Select(task => task.Title).ToList();
        List<ShutdownTaskRow> rows = _tasks.History
            .Where(checklist => checklist.SessionStart is DateTimeOffset start && DateOnly.FromDateTime(ToLocal(start)) == date)
            .SelectMany(checklist => checklist.Tasks.Where(task => !task.IsDone && !_tasks.WasMoved(task.Id)))
            .DistinctBy(task => task.Title, StringComparer.Ordinal)
            .Where(task => !waiting.Contains(task.Title))
            .Select(task => new ShutdownTaskRow(task.Title, ExistingCategory(task.CategoryId)))
            .ToList();
        return GroupByCategory(rows);
    }

    // Categories in their own order, then the rows with no category last.
    private List<ShutdownTaskGroup> GroupByCategory(IReadOnlyList<ShutdownTaskRow> rows)
    {
        List<ShutdownTaskGroup> groups = _tasks.Categories
            .Select(category => new ShutdownTaskGroup(
                category.Name, TaskCategoryBrushes.Of(category.Color), true, rows.Where(row => row.CategoryId == category.Id).ToList()))
            .Where(group => group.Rows.Count > 0)
            .ToList();
        List<ShutdownTaskRow> uncategorised = rows.Where(row => row.CategoryId is null).ToList();
        if (uncategorised.Count > 0)
        {
            groups.Add(new ShutdownTaskGroup("No category", TaskCategoryBrushes.Of(null), false, uncategorised));
        }

        return groups;
    }

    // A carried task keeps its category only while that category still exists.
    private Guid? ExistingCategory(Guid? categoryId)
    {
        return _tasks.Categories.Any(category => category.Id == categoryId) ? categoryId : null;
    }

    // Later items due within two weeks (or overdue), and tomorrow's and the next two weeks' events of the calendars that are shown.
    private (IReadOnlyList<ShutdownLaterRow>, IReadOnlyList<ShutdownAheadDay>) LookAheadFrom(DateOnly date)
    {
        List<ShutdownLaterRow> later = (_tasks.Later?.Items ?? [])
            .Where(item => item.DueOn is DateOnly due && due <= date.AddDays(LaterDueWithinDays))
            .OrderBy(item => item.DueOn)
            .Select(item => new ShutdownLaterRow(item.Title, DueText(item.DueOn!.Value, date)))
            .ToList();
        var days = new List<ShutdownAheadDay>();
        for (int offset = 1; offset <= LookAheadDays; offset++)
        {
            DateOnly day = date.AddDays(offset);
            IReadOnlyList<CalendarEntry> entries = _calendar.EventsOn(day);
            if (entries.Count == 0)
            {
                continue;
            }

            List<string> lines = entries.Take(MaxEventsPerDay).Select(entry => EventLine(entry, day)).ToList();
            if (entries.Count > MaxEventsPerDay)
            {
                lines.Add($"+{entries.Count - MaxEventsPerDay} more");
            }

            string heading = day.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
            days.Add(new ShutdownAheadDay(offset == 1 ? $"Tomorrow · {heading}" : heading, lines));
        }

        return (later, days);
    }

    private static string DueText(DateOnly due, DateOnly date)
    {
        string day = due.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
        return due < date ? $"overdue · {day}" : due == date ? "today" : due == date.AddDays(1) ? $"tomorrow · {day}" : day;
    }

    private static string EventLine(CalendarEntry entry, DateOnly day)
    {
        CalendarEvent calendarEvent = entry.Event;
        bool allDay = calendarEvent.IsAllDay || calendarEvent.LocalStart.Date < day.ToDateTime(TimeOnly.MinValue);
        string time = allDay ? "All day" : calendarEvent.LocalStart.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return $"{time} · {calendarEvent.Title}";
    }

    // Up to three rows, each with tomorrow's day parts to put its task in (sleep left out); the first work part is the default.
    private IReadOnlyList<ShutdownPlanRowViewModel> PlanRowsFor(DateOnly date)
    {
        DateOnly tomorrow = date.AddDays(1);
        DateTime start = tomorrow.ToDateTime(TimeOnly.MinValue);
        List<PlannedPart> parts = _configuration.Current.DayPlan.PartsIn(new LocalWindow(start, start.AddDays(1)))
            .Where(part => part.Category.Behavior != CategoryBehavior.Sleep && DateOnly.FromDateTime(part.Window.Start) == tomorrow)
            .OrderBy(part => part.Window.Start)
            .ToList();
        if (parts.Count == 0)
        {
            return [];
        }

        List<ShutdownPartChoice> choices = parts
            .Select(part => new ShutdownPartChoice(
                $"{part.Window.Start.ToString("h:mm tt", CultureInfo.InvariantCulture)} · {part.Name}",
                PlannedSlot.At(tomorrow, part.Part.Id, TimeOnly.FromDateTime(part.Window.Start))))
            .ToList();
        int firstWork = Math.Max(0, parts.FindIndex(part => part.Category.Behavior.IsWork()));
        return Enumerable.Range(0, PlanRowCount).Select(_ => new ShutdownPlanRowViewModel(_tasks, choices, firstWork)).ToList();
    }

    private ShutdownReviewViewModel ReviewFor(DateOnly date)
    {
        string focus = FocusFor?.Invoke(date) is GoalProgress progress ? progress.Text : "No focus counted yet";
        var done = _tasks.History.Append(_tasks.Current)
            .SelectMany(checklist => checklist.Tasks)
            .Where(task => task.DoneAt is DateTimeOffset at && DateOnly.FromDateTime(ToLocal(at)) == date)
            .DistinctBy(task => task.Id)
            .ToList();
        var lines = new List<string>();
        if (done.Count > 0)
        {
            lines.Add(done.Count == 1 ? "1 task done" : $"{done.Count} tasks done");
            lines.AddRange(done.Take(MaxDoneTitles).Select(task => task.Title));
            if (done.Count > MaxDoneTitles)
            {
                lines.Add($"+{done.Count - MaxDoneTitles} more");
            }
        }

        ShutdownReview? earlier = _reviews.Of(date);
        return new ShutdownReviewViewModel(focus, lines, earlier?.Feeling, earlier?.Note ?? string.Empty);
    }

    // Drift is read from the activity log, so it arrives a moment later; with history off it is left out.
    private async Task FillDriftAsync(ShutdownCardViewModel card, DateOnly date)
    {
        if (DriftFor is null)
        {
            return;
        }

        try
        {
            TimeSpan? drift = await DriftFor(date);
            if (drift is TimeSpan off && Current == card)
            {
                card.Review.DriftText = off < TimeSpan.FromMinutes(1) ? "No drift in your sessions" : $"{GoalProgress.Duration(off)} off track in sessions";
            }
        }
        catch (SqliteException)
        {
            // Left out this time.
        }
    }

    // ---- Writing a step's choices ----

    private async Task ApplyStepAsync(ShutdownStep step, ShutdownCardViewModel card, DateOnly date)
    {
        switch (step)
        {
            case ShutdownStep.Capture:
                await ApplyCaptureAsync(card);
                break;
            case ShutdownStep.Unfinished:
                await ApplyUnfinishedAsync(card, date);
                break;
            case ShutdownStep.PlanTomorrow:
                await ApplyPlanAsync(card);
                break;
            case ShutdownStep.Review:
                if (NeedsReviewSave(card, date))
                {
                    await _reviews.SaveReviewAsync(date, card.Review.Feeling, card.Review.Note);
                }

                break;
        }
    }

    // Every parked thought gets a home: ticked, a task on the next session's list (Later when that list is full), or Later with a date.
    private async Task ApplyCaptureAsync(ShutdownCardViewModel card)
    {
        foreach (ShutdownComebackRow row in card.Comebacks)
        {
            switch (row.Choice)
            {
                case ShutdownComebackChoice.Done:
                    await _comebacks.TickAsync(row.Id);
                    break;
                case ShutdownComebackChoice.Task:
                    (AddToTodayResult result, _) = await _tasks.AddComebackToTodayAsync(row.Id);
                    if (result != AddToTodayResult.Added)
                    {
                        await SendToLaterAsync(row, dueOn: null);
                    }

                    break;
                case ShutdownComebackChoice.Later:
                    await SendToLaterAsync(row, row.DueDate is DateTime due ? DateOnly.FromDateTime(due) : null);
                    break;
            }
        }
    }

    private async Task SendToLaterAsync(ShutdownComebackRow row, DateOnly? dueOn)
    {
        Guid? laterId = await _tasks.MoveComebackToLaterAsync(row.Id);
        if (laterId is Guid id && dueOn is DateOnly due && _tasks.Later is LaterStore later)
        {
            await later.EditAsync(id, item => item with { DueOn = due });
        }
    }

    // Ticked ones are done; the rest carry into the next session's list.
    private async Task ApplyUnfinishedAsync(ShutdownCardViewModel card, DateOnly date)
    {
        foreach (ShutdownTaskRow row in card.Unfinished.Where(row => !row.IsDone))
        {
            await _tasks.AddAsync(row.Title, null, ExistingCategory(row.CategoryId));
        }

        HashSet<string> ticked = card.Unfinished.Where(row => row.IsDone).Select(row => row.Title).ToHashSet(StringComparer.Ordinal);
        if (ticked.Count > 0)
        {
            await _tasks.MarkHistoryDoneAsync(date, ticked);
        }
    }

    // Tomorrow's first task (the quick path) goes to the top of the checklist; each planned row goes on the Later list, put in its part.
    private async Task ApplyPlanAsync(ShutdownCardViewModel card)
    {
        string first = card.TomorrowTask.Trim();
        // One already waiting on the list (carried by the step before) isn't added a second time.
        bool alreadyWaiting = _tasks.Current.Tasks.Any(task => !task.IsDone && task.Title == first);
        if (first.Length > 0 && !alreadyWaiting)
        {
            // Tomorrow's first task goes to the top of the checklist; one chosen from a list moves there with its steps and leaves that list.
            if (card.TomorrowSource is (PulledListKind kind, Guid id))
            {
                await _tasks.TakeFirstFromListAsync(kind, id, first);
            }
            else
            {
                await _tasks.AddFirstAsync(first);
            }
        }

        // A title already on the next session's list isn't planned a second time.
        var planned = new HashSet<string>(_tasks.Current.Tasks.Select(task => task.Title), StringComparer.Ordinal);
        foreach (ShutdownPlanRowViewModel row in card.PlanRows)
        {
            if (row.Title.Length > 0 && row.Slot is PlannedSlot slot && planned.Add(row.Title))
            {
                await _tasks.PlanForTomorrowAsync(row.Source, row.Title, slot);
            }
        }

        // Applied: the inputs are emptied, so "Not now" or an auto-close doesn't keep them a second time.
        card.TomorrowTask = string.Empty;
        foreach (ShutdownPlanRowViewModel row in card.PlanRows)
        {
            row.Text = string.Empty;
        }
    }

    // "Work over for today": the day is recorded, the card closes, and Today holds the line until the next work part.
    private async Task FinishAsync(ShutdownCardViewModel card, DateOnly date)
    {
        DateTimeOffset now = _sessions.Now;
        DateTimeOffset keptFrom = now.AddDays(-KeptDays);
        // Only a finish once work is over (or within the reminder minutes before it ends) settles the day; a morning finish doesn't.
        DateTime localNow = LocalNow();
        bool afterWork = WorkEnd(date, localNow) is not DateTime workEnd || localNow >= workEnd.AddMinutes(-Settings.ReminderMinutes);
        List<ShutdownDay> days = (_state.Days ?? [])
            .Where(day => day.Date != date && day.CompletedAt >= keptFrom)
            .Append(new ShutdownDay(date, now, card.Checked.ToList(), card.ClosingLine, afterWork))
            .ToList();
        _state = _state with { Days = days, NotNowOn = null, ReturnAt = null };
        await CloseCardAsync();
        RefreshWorkOver(LocalNow());
    }

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, _state);
        }
        catch (SqliteException)
        {
            // Best effort: at worst the card shows once more after a restart.
        }
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        CheckNow();
    }

    // One typed task kept from a card: where it was ("Row" null = tomorrow's first task), the part picked, and its Auto-saved item.
    private sealed record ParkedText(int? Row, int? PartIndex, string Title, Guid AutoSavedId);

    private sealed record ParkedDraft(DateOnly Date, IReadOnlyList<ParkedText> Texts);
}
