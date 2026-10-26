using System.Globalization;
using System.Text.Json;
using AccountabiliBuddy.App.Services.Remote;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Link;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Applies what the user did on the phone for this PC (ADR 26, 42): the actions in the other devices' sync files that
/// are meant for this PC, each once. Tick a task or subtask (left done if already done), park a thought (a come-back),
/// tick a come-back, step away or come back (only within 10 minutes of doing it, so an old one never replays); start a
/// session or ask to end one early (only within 2 minutes), end early after the offer's wait (until its allowed-from
/// time plus 10 minutes), and edit the day plan (within a day), all by the same rules as the link
/// (<see cref="RemoteSessionControl"/>, <see cref="RemotePlanEdits"/>). An action past its freshness is refused, never
/// applied late. The ids applied are kept 14 days (app-owned document "sync.actions") and go back in this PC's file as
/// <see cref="SyncFile.Acks"/>, so the phone stops sending them, and every action gets a <see cref="SyncActionResult"/>
/// (kinds this version doesn't know: "not supported"). Ending early is two steps: the first makes an
/// <see cref="SyncEndEarlyOffer"/> with this PC's wait on this PC's clock; the second ends the session, recording only
/// "reason given on &lt;phone&gt;, N characters" (the reason never leaves the phone). Use from the UI thread.
/// </summary>
public sealed class SyncActionsService
{
    private const string DocumentKey = "sync.actions";
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(14);
    private static readonly TimeSpan StepAwayFresh = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ControlFresh = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PlanEditFresh = TimeSpan.FromDays(1);

    private readonly SqliteJsonDocumentStore _store;
    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly SessionTasksService _tasks;
    private readonly ComebackStore _comebacks;
    private readonly StepAwayService _stepAway;
    private readonly SessionController _sessions;
    private readonly RemoteSessionControl _control;
    private readonly RemotePlanEdits _planEdits;
    private readonly TimeProvider _time;
    private List<AppliedAction> _applied = [];
    private bool _applying;

    // The open end-early offer and the phase it was made in (a return from stepping away moves the phase's start, so
    // the phase is told by its kind and count instead). Kept in memory: after a restart the phone asks again.
    private SyncEndEarlyOffer? _offer;
    private PhaseKey? _offerPhase;

    public SyncActionsService(
        SqliteJsonDocumentStore store, ReceivedDevicesService received, SyncIdentityService identity, SessionTasksService tasks,
        ComebackStore comebacks, StepAwayService stepAway, SessionController sessions, RemoteSessionControl control,
        RemotePlanEdits planEdits, TimeProvider time)
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
    public SyncEndEarlyOffer? EndEarlyOffer =>
        _offer is { } offer && _time.GetUtcNow() <= offer.AllowedFrom + SyncEndEarlyOffer.ValidFor ? offer : null;

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

    /// <summary>Applies the received actions for this PC not applied yet.</summary>
    public async Task ApplyAsync()
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            var done = _applied.Select(action => action.Id).ToHashSet();
            List<SentAction> fresh = ActionsForThisPc()
                .Where(sent => !done.Contains(sent.Action.Id))
                .OrderBy(sent => sent.Action.At)
                .ToList();
            if (fresh.Count == 0)
            {
                return;
            }

            foreach (SentAction sent in fresh)
            {
                SyncActionResult result = await ApplyOneAsync(sent.Action, sent.From, now);
                _applied.Add(new AppliedAction(sent.Action.Id, now, result));
            }

            _applied = _applied.Where(action => now - action.At < KeptFor).ToList();
            await _store.SaveAsync(DocumentKey, _applied);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (SqliteException)
        {
            // Applied for now; the ids are written with the next change.
        }
        finally
        {
            _applying = false;
        }
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
            default:
                return Refused(action, SyncActionResult.NotSupportedMessage);
        }
    }

    // Like the link's start: refused while a session runs, the profile must exist, the length 1 minute to 12 hours.
    private async Task<SyncActionResult> StartAsync(SyncAction action, DateTimeOffset now)
    {
        if (now - action.At > ControlFresh)
        {
            return TooLate(action);
        }

        if (Read<SyncStartRequest>(action.Text) is not SyncStartRequest request)
        {
            return Refused(action, "The PC couldn't read this request.");
        }

        string? error;
        if (!request.NoSetEnd)
        {
            error = _control.Start(request.ProfileId, request.Minutes);
        }
        else if (request.ProfileId is Guid profileId && profileId != _control.CountUpProfileId)
        {
            error = _control.StartCountUp(profileId);
        }
        else
        {
            // "Quick Start with Default" (made on first use, so a phone may not know its id yet).
            error = await _control.StartCountUpAsync();
        }

        return error is null ? Ok(action, "Started.") : Refused(action, error);
    }

    // The first step: an offer with this PC's wait, or, for a break or a session that ends on command, the end at once.
    private SyncActionResult BeginEndEarly(SyncAction action, DateTimeOffset now)
    {
        if (now - action.At > ControlFresh)
        {
            return TooLate(action);
        }

        SessionState state = _sessions.State;
        if (_control.EndEarlyTerms() is not LinkEndEarlyTerms terms)
        {
            return Refused(action, "No session is running.");
        }

        if (!state.IsEnforcing || _sessions.EndsOnCommand)
        {
            ClearOffer();
            _control.TryEnd(string.Empty, out string? notice);
            if (notice is not null)
            {
                EndedFromDevice?.Invoke(this, notice);
            }

            return Ok(action, "Session ended.");
        }

        _offer = new SyncEndEarlyOffer(Guid.NewGuid(), state.PhaseStartedAt ?? now, terms.AllowedFrom, terms.MinReasonLength, terms.Note);
        _offerPhase = PhaseKey.Of(state);
        return Ok(action);
    }

    // The second step: the offer's id, its phase, the wait over on this PC's clock, not past its 10 minutes, a long
    // enough reason (its length only).
    private SyncActionResult EndEarly(SyncAction action, string from, DateTimeOffset now)
    {
        if (_offer is not SyncEndEarlyOffer offer || action.Item != offer.RequestId)
        {
            return Refused(action, "Ask to end the session first (the session may have changed meanwhile).");
        }

        if (!_sessions.State.IsActive || PhaseKey.Of(_sessions.State) != _offerPhase)
        {
            ClearOffer();
            return Refused(action, "Ask to end the session first (the session may have changed meanwhile).");
        }

        if (now > offer.AllowedFrom + SyncEndEarlyOffer.ValidFor)
        {
            ClearOffer();
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

        ClearOffer();
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

    // ADR 19 by ADR 42's transport: the same decision as over the link.
    private async Task<SyncActionResult> EditPlanAsync(SyncAction action, DateTimeOffset now)
    {
        if (now - action.At > PlanEditFresh)
        {
            return TooLate(action);
        }

        if (Read<SyncDayPlanEditRequest>(action.Text) is not { BasedOn: not null, Plan.ValueKind: JsonValueKind.Object } request)
        {
            return Refused(action, "The PC couldn't read this day plan.");
        }

        LinkDayPlanAnswer answer = await _planEdits.ApplyAsync(new LinkDayPlanEdit(request.BasedOn, request.Plan, request.EditedAt));
        return answer.Status switch
        {
            LinkDayPlanStatus.Applied => new SyncActionResult(action.Id, true, answer.Message, answer.CurrentVersion),
            LinkDayPlanStatus.Conflict => new SyncActionResult(action.Id, false, answer.Message, answer.CurrentVersion, answer.CurrentEditedAt),
            _ => new SyncActionResult(action.Id, false, answer.Message),
        };
    }

    // An offer belongs to its phase: a session ending or a new phase drops it, and the file loses it.
    private void OnSessionStateChanged(SessionState state)
    {
        if (_offer is not null && (!state.IsActive || PhaseKey.Of(state) != _offerPhase))
        {
            ClearOffer();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ClearOffer()
    {
        _offer = null;
        _offerPhase = null;
    }

    private IEnumerable<SentAction> ActionsForThisPc()
    {
        Guid self = _identity.DeviceId;
        return _received.Devices
            .SelectMany(device => (device.File.Actions ?? []).Select(action => new SentAction(action, device.File.Device.Name)))
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
            return JsonSerializer.Deserialize<T>(text, LinkFraming.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SyncActionResult Ok(SyncAction action, string? message = null) => new(action.Id, true, message);

    private static SyncActionResult Refused(SyncAction action, string message) => new(action.Id, false, message);

    private static SyncActionResult TooLate(SyncAction action) => new(action.Id, false, SyncActionResult.TooLateMessage);

    // A task or subtask of the current checklist that isn't ticked yet (ticking twice would untick it).
    private bool IsOpen(Guid id) => _tasks.Current.Tasks
        .SelectMany(task => task.Subtasks.Select(subtask => (subtask.Id, subtask.IsDone)).Prepend((task.Id, task.IsDone)))
        .Any(entry => entry.Id == id && !entry.IsDone);

    /// <param name="Result">Null for actions applied before this PC answered them (ADR 42).</param>
    private sealed record AppliedAction(Guid Id, DateTimeOffset At, SyncActionResult? Result = null);

    private sealed record SentAction(SyncAction Action, string From);

    // Which phase of which session an offer belongs to; the phase's start moves while stepping away, these don't.
    private sealed record PhaseKey(SessionMode? Mode, SessionPhase Phase, Guid? ProfileId, int CompletedFocusCycles)
    {
        public static PhaseKey Of(SessionState state) => new(state.Mode, state.Phase, state.ProfileId, state.CompletedFocusCycles);
    }
}
