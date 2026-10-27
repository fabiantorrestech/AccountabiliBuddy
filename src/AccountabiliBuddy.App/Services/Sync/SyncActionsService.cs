using System.Globalization;
using System.Text.Json;
using AccountabiliBuddy.App.Services.Remote;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Applies what the user did on the phone for this PC (ADR 26, 42): the actions in the other devices' sync files that
/// are meant for this PC, each once. Tick a task or subtask (left done if already done), park a thought (a come-back),
/// tick a come-back, step away or come back (only within 10 minutes of doing it, so an old one never replays); start a
/// session or ask to end one early (only within 2 minutes), end early after the offer's wait (until its allowed-from
/// time plus 10 minutes), edit the day plan (within a day), and control the noise player (within 2 minutes, by its own
/// buttons' rules: <see cref="RemoteNoiseControl"/>), add, rename or reorder the running session's tasks (ADR 48, within 2
/// minutes; <see cref="SessionTasksService.AddRemoteAsync"/> and its siblings keep the three-tasks rule), all by the same rules as the PC's own buttons
/// (<see cref="RemoteSessionControl"/>, <see cref="RemotePlanEdits"/>). An action past its freshness is refused, never
/// applied late; freshness counts from when this PC first saw the action (its file's arrival, on this PC's clock), and
/// an action the phone made more than a day ago is refused too, so neither clock's skew decides alone. The ids applied
/// are kept 14 days (app-owned document "sync.actions") and go back in this PC's file as <see cref="SyncFile.Acks"/>,
/// so the phone stops sending them, and every action gets a <see cref="SyncActionResult"/> (kinds this version doesn't
/// know: "unsupported"). Ending early is two steps: the first makes an offer (<see cref="SyncEndEarlyOffers"/>) with
/// this PC's wait on this PC's clock; the second ends the session, recording only "reason given on &lt;phone&gt;, N
/// characters" (the reason never leaves the phone). Use from the UI thread.
/// </summary>
public sealed class SyncActionsService
{
    private const string DocumentKey = "sync.actions";
    private const string AskFirstMessage = "Ask to end the session first (the session may have changed meanwhile).";
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(14);
    private static readonly TimeSpan StepAwayFresh = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ControlFresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PlanEditFresh = TimeSpan.FromDays(1);

    // A control action the phone made longer ago than this is refused whenever it arrives.
    private static readonly TimeSpan MadeTooLongAgo = TimeSpan.FromDays(1);

    private readonly SqliteJsonDocumentStore _store;
    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly SessionTasksService _tasks;
    private readonly ComebackStore _comebacks;
    private readonly StepAwayService _stepAway;
    private readonly SessionController _sessions;
    private readonly RemoteSessionControl _control;
    private readonly RemotePlanEdits _planEdits;
    private readonly RemoteNoiseControl _noise;
    private readonly TimeProvider _time;
    private readonly SyncEndEarlyOffers _offers = new();

    // When this PC first saw each action still in a file (its file's arrival). In memory: applied ones are kept by id.
    private readonly Dictionary<Guid, DateTimeOffset> _firstSeen = [];
    private List<AppliedAction> _applied = [];
    private bool _applying;
    private bool _rerunRequested;

    public SyncActionsService(
        SqliteJsonDocumentStore store, ReceivedDevicesService received, SyncIdentityService identity, SessionTasksService tasks,
        ComebackStore comebacks, StepAwayService stepAway, SessionController sessions, RemoteSessionControl control,
        RemotePlanEdits planEdits, RemoteNoiseControl noise, TimeProvider time)
    {
        _store = store;
        _received = received;
        _identity = identity;
        _tasks = tasks;
        _comebacks = comebacks;
        _stepAway = stepAway;
        _sessions = sessions;
        _control = control;
        _planEdits = planEdits;
        _noise = noise;
        _time = time;
        sessions.StateChanged += (_, state) => OnSessionStateChanged(state);
    }

    /// <summary>Raised after actions were applied: this PC's file is written again with the new acks and results.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with the notice text when the phone ended a focus session through the sync files.</summary>
    public event EventHandler<string>? EndedFromDevice;

    /// <summary>The ids applied in the last 14 days, for this PC's sync file.</summary>
    public IReadOnlyList<Guid> Acks => _applied.Select(action => action.Id).ToList();

    /// <summary>The answers for this PC's file: one per applied action that is still in the phone's file.</summary>
    public IReadOnlyList<SyncActionResult> Results
    {
        get
        {
            HashSet<Guid> stillSent = ActionsForThisPc().Select(sent => sent.Action.Id).ToHashSet();
            return _applied
                .Where(action => action.Result is not null && stillSent.Contains(action.Id))
                .Select(action => action.Result!)
                .ToList();
        }
    }

    /// <summary>The open end-early offer for this PC's file; null when there's none or it ran out.</summary>
    public SyncEndEarlyOffer? EndEarlyOffer => _offers.Current(_time.GetUtcNow());

    public void LoadBlocking()
    {
        try
        {
            _applied = Task.Run(() => _store.LoadAsync<List<AppliedAction>>(DocumentKey)).GetAwaiter().GetResult() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            _applied = [];
        }
    }

    /// <summary>Applies the received actions for this PC not applied yet; a call while applying runs again right after.</summary>
    public async Task ApplyAsync()
    {
        if (_applying)
        {
            _rerunRequested = true;
            return;
        }

        _applying = true;
        try
        {
            do
            {
                _rerunRequested = false;
                await ApplyNewAsync();
            }
            while (_rerunRequested);
        }
        finally
        {
            _applying = false;
        }
    }

    private async Task ApplyNewAsync()
    {
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            List<SentAction> sent = ActionsForThisPc().ToList();
            NoteFirstSeen(sent, now);
            var applied = _applied.Select(action => action.Id).ToHashSet();
            List<SentAction> fresh = sent
                .Where(action => !applied.Contains(action.Action.Id))
                .OrderBy(action => action.Action.At)
                .ToList();
            if (fresh.Count == 0)
            {
                return;
            }

            foreach (SentAction action in fresh)
            {
                SyncActionResult result = await ApplyOneAsync(action.Action, action.From, now);
                _applied.Add(new AppliedAction(action.Action.Id, now, result));
            }

            _applied = _applied.Where(action => now - action.At < KeptFor).ToList();
            await _store.SaveAsync(DocumentKey, _applied);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (SqliteException)
        {
            // Applied for now; the ids are saved with the next change. The file still gets the new acks, results and offer.
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // The arrival of the file an action first came in, on this PC's clock (never later than now); forgotten once it
    // leaves the phone's file.
    private void NoteFirstSeen(IReadOnlyList<SentAction> sent, DateTimeOffset now)
    {
        foreach (SentAction action in sent)
        {
            if (!_firstSeen.ContainsKey(action.Action.Id))
            {
                _firstSeen[action.Action.Id] = action.ReceivedAt < now ? action.ReceivedAt : now;
            }
        }

        HashSet<Guid> stillSent = sent.Select(action => action.Action.Id).ToHashSet();
        foreach (Guid gone in _firstSeen.Keys.Where(id => !stillSent.Contains(id)).ToList())
        {
            _firstSeen.Remove(gone);
        }
    }

    // Older than its freshness since this PC first saw it, or made on the phone more than a day ago.
    private bool IsLate(SyncAction action, TimeSpan freshness, DateTimeOffset now)
    {
        DateTimeOffset firstSeen = _firstSeen.TryGetValue(action.Id, out DateTimeOffset seen) ? seen : now;
        return now - firstSeen > freshness || now - action.At > MadeTooLongAgo;
    }

    private async Task<SyncActionResult> ApplyOneAsync(SyncAction action, string from, DateTimeOffset now)
    {
        switch (action.Kind)
        {
            case SyncAction.KindTickTask:
                if (action.Item is Guid item && IsOpen(item))
                {
                    await _tasks.ToggleAsync(item);
                }

                return Ok(action);
            case SyncAction.KindPark:
                if (!string.IsNullOrWhiteSpace(action.Text))
                {
                    await _comebacks.AddAsync(action.Text.Trim());
                }

                return Ok(action);
            case SyncAction.KindTickComeback:
                if (action.Item is Guid comeback)
                {
                    await _comebacks.TickAsync(comeback);
                }

                return Ok(action);
            case SyncAction.KindStepAway:
                if (now - action.At >= StepAwayFresh)
                {
                    return TooLate(action);
                }

                if (!_stepAway.IsAway)
                {
                    _stepAway.StepAway(AwayKind.Break);
                }

                return Ok(action);
            case SyncAction.KindBack:
                if (now - action.At >= StepAwayFresh)
                {
                    return TooLate(action);
                }

                if (_stepAway.IsAway)
                {
                    _stepAway.Return();
                }

                return Ok(action);
            case SyncAction.KindStart:
                return await StartAsync(action, now);
            case SyncAction.KindEndEarlyBegin:
                return BeginEndEarly(action, now);
            case SyncAction.KindEndEarly:
                return EndEarly(action, from, now);
            case SyncAction.KindDayPlanEdit:
                return await EditPlanAsync(action, now);
            case SyncAction.KindNoise:
                return await ControlNoiseAsync(action, now);
            case SyncAction.KindTaskAdd:
            case SyncAction.KindTaskEdit:
            case SyncAction.KindTaskReorder:
                return await ChangeTasksAsync(action, now);
            default:
                return Unsupported(action);
        }
    }

    // Refused while a session runs, the profile must exist, the length 1 minute to 12 hours.
    private async Task<SyncActionResult> StartAsync(SyncAction action, DateTimeOffset now)
    {
        if (IsLate(action, ControlFresh, now))
        {
            return TooLate(action);
        }

        if (Read<SyncStartRequest>(action.Text) is not SyncStartRequest request)
        {
            return Invalid(action, "The PC couldn't read this request.");
        }

        // Three kinds of request: a profile for some minutes; "no set end" (explicit: it counts up); and a Quick Start, which
        // names no length and runs for the PC's remembered one (the default profile when it names no profile; made on first use,
        // so a phone may not know its id yet).
        string? error;
        if (request.NoSetEnd)
        {
            error = request.ProfileId is Guid openProfile
                ? _control.StartOpenEnded(openProfile)
                : await _control.StartOpenEndedWithDefaultAsync();
        }
        else if (request.Minutes is null)
        {
            error = request.ProfileId is Guid quickProfile
                ? _control.StartQuick(quickProfile)
                : await _control.StartWithDefaultAsync();
        }
        else
        {
            error = _control.Start(request.ProfileId, request.Minutes);
        }

        return error is null ? Ok(action, "Started.") : Refused(action, error);
    }

    // The first step: an offer with this PC's wait, or, for a break or a session that ends on command, the end at once.
    private SyncActionResult BeginEndEarly(SyncAction action, DateTimeOffset now)
    {
        if (IsLate(action, ControlFresh, now))
        {
            return TooLate(action);
        }

        SessionState state = _sessions.State;
        if (_control.EndEarlyTerms() is not EndEarlyTerms terms)
        {
            return Refused(action, "No session is running.");
        }

        if (!state.IsEnforcing || _sessions.EndsOnCommand)
        {
            _offers.Clear();
            _control.TryEnd(string.Empty, out string? notice);
            if (notice is not null)
            {
                EndedFromDevice?.Invoke(this, notice);
            }

            return Ok(action, "Session ended.");
        }

        _offers.Make(state, terms.AllowedFrom, terms.MinReasonLength, terms.Note, now);
        return Ok(action);
    }

    // The second step: the offer's id, its phase, the wait over on this PC's clock, not past its 10 minutes, a long
    // enough reason (its length only).
    private SyncActionResult EndEarly(SyncAction action, string from, DateTimeOffset now)
    {
        if (_offers.Open is not SyncEndEarlyOffer offer || action.Item != offer.RequestId)
        {
            return Refused(action, AskFirstMessage);
        }

        if (!_offers.BelongsTo(_sessions.State))
        {
            _offers.Clear();
            return Refused(action, AskFirstMessage);
        }

        if (now > offer.AllowedFrom + SyncEndEarlyOffer.ValidFor || now - action.At > MadeTooLongAgo)
        {
            _offers.Clear();
            return TooLate(action);
        }

        TimeSpan left = offer.AllowedFrom - now;
        if (left > TimeSpan.Zero)
        {
            return Refused(action, $"Wait until the countdown ends ({Math.Ceiling(left.TotalSeconds):0} s left).");
        }

        if (!int.TryParse(action.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int length) || length < offer.MinReasonLength)
        {
            return Refused(action, $"The reason needs at least {offer.MinReasonLength} characters.");
        }

        _offers.Clear();
        string reason = $"reason given on {from}, {length} characters";
        if (!_control.TryEnd(reason, out string? notice))
        {
            return Refused(action, "No session is running.");
        }

        if (notice is not null)
        {
            EndedFromDevice?.Invoke(this, notice);
        }

        return Ok(action, "Session ended.");
    }

    // ADR 19 by ADR 42's transport.
    private async Task<SyncActionResult> EditPlanAsync(SyncAction action, DateTimeOffset now)
    {
        if (IsLate(action, PlanEditFresh, now))
        {
            return TooLate(action);
        }

        if (Read<SyncDayPlanEditRequest>(action.Text) is not { BasedOn: not null, Plan.ValueKind: JsonValueKind.Object } request
            || request.EditedAt == default)
        {
            return Invalid(action, "The PC couldn't read this day plan.");
        }

        DayPlanAnswer answer = await _planEdits.ApplyAsync(new DayPlanEdit(request.BasedOn, request.Plan, request.EditedAt));
        return answer.Status switch
        {
            DayPlanStatus.Applied => new SyncActionResult(action.Id, true, SyncActionStatus.Applied, answer.Message, answer.CurrentVersion),
            DayPlanStatus.Conflict => new SyncActionResult(
                action.Id, false, SyncActionStatus.Conflict, answer.Message, answer.CurrentVersion, answer.CurrentEditedAt),
            DayPlanStatus.Locked => new SyncActionResult(action.Id, false, SyncActionStatus.Locked, answer.Message),
            _ => Invalid(action, answer.Message),
        };
    }

    // ADR 36 by ADR 42's transport: the noise player's own buttons. Never worth applying late, and allowed in a session.
    private async Task<SyncActionResult> ControlNoiseAsync(SyncAction action, DateTimeOffset now)
    {
        if (IsLate(action, ControlFresh, now))
        {
            return TooLate(action);
        }

        if (Read<SyncNoiseRequest>(action.Text) is not SyncNoiseRequest request || !RemoteNoiseControl.IsValid(request))
        {
            return Invalid(action, "The PC couldn't read this request.");
        }

        string? error = await _noise.ApplyAsync(request, _identity.DeviceName);
        return error is null ? Ok(action) : Refused(action, error);
    }

    // ADR 48: the running session's checklist, changed through SessionTasksService's rules (three tasks, nothing in a phase's
    // last minute, nothing without a running session). Never worth applying late: the checklist moves on meanwhile.
    private async Task<SyncActionResult> ChangeTasksAsync(SyncAction action, DateTimeOffset now)
    {
        if (IsLate(action, ControlFresh, now))
        {
            return TooLate(action);
        }

        if (Read<SyncTaskRequest>(action.Text) is not SyncTaskRequest request)
        {
            return Invalid(action, "The PC couldn't read this request.");
        }

        RemoteTaskResult result = action.Kind switch
        {
            SyncAction.KindTaskAdd => await _tasks.AddRemoteAsync(action.Item ?? Guid.Empty, request),
            SyncAction.KindTaskEdit when action.Item is Guid task => await _tasks.EditRemoteAsync(task, request),
            SyncAction.KindTaskReorder => await _tasks.ReorderRemoteAsync(request.Order),
            _ => RemoteTaskResult.NotValid("The PC couldn't read this request."),
        };
        if (result.Ok)
        {
            return Ok(action, result.Message);
        }

        return result.Invalid ? Invalid(action, result.Message ?? "The PC couldn't read this request.") : Refused(action, result.Message ?? "Refused.");
    }

    // An offer belongs to its phase: a session ending or a new phase drops it, and the file loses it.
    private void OnSessionStateChanged(SessionState state)
    {
        if (_offers.Open is not null && !_offers.BelongsTo(state))
        {
            _offers.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private IEnumerable<SentAction> ActionsForThisPc()
    {
        Guid self = _identity.DeviceId;
        return _received.Devices
            .SelectMany(device => (device.File.Actions ?? []).Select(action => new SentAction(action, device.File.Device.Name, device.ReceivedAt)))
            .Where(sent => sent.Action.Target == self);
    }

    private static T? Read<T>(string? text)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, SyncJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SyncActionResult Ok(SyncAction action, string? message = null) => new(action.Id, true, SyncActionStatus.Applied, message);

    private static SyncActionResult Refused(SyncAction action, string message) => new(action.Id, false, SyncActionStatus.Refused, message);

    private static SyncActionResult Invalid(SyncAction action, string message) => new(action.Id, false, SyncActionStatus.Invalid, message);

    private static SyncActionResult TooLate(SyncAction action) => new(action.Id, false, SyncActionStatus.Late, SyncActionResult.TooLateMessage);

    private static SyncActionResult Unsupported(SyncAction action) =>
        new(action.Id, false, SyncActionStatus.Unsupported, SyncActionResult.NotSupportedMessage);

    // A task or subtask of the current checklist that isn't ticked yet (ticking twice would untick it).
    private bool IsOpen(Guid id) => _tasks.Current.Tasks
        .SelectMany(task => task.Subtasks.Select(subtask => (subtask.Id, subtask.IsDone)).Prepend((task.Id, task.IsDone)))
        .Any(entry => entry.Id == id && !entry.IsDone);

    /// <param name="Result">Null for actions applied before this PC answered them (ADR 42).</param>
    private sealed record AppliedAction(Guid Id, DateTimeOffset At, SyncActionResult? Result = null);

    /// <param name="From">The sending device's name.</param>
    /// <param name="ReceivedAt">When its newest file arrived here (this PC's clock).</param>
    private sealed record SentAction(SyncAction Action, string From, DateTimeOffset ReceivedAt);
}
