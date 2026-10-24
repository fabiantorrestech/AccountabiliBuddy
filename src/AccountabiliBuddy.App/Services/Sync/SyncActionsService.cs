using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Applies what the user did on the phone for this PC (ADR 26): the actions in the other devices' sync files that are
/// meant for this PC, each once. Tick a task or subtask (left done if already done), park a thought (a come-back),
/// tick a come-back, step away or come back (only within 10 minutes of doing it, so an old one never replays). The ids
/// applied are kept 14 days (app-owned document "sync.actions") and go back in this PC's file as <see cref="SyncFile.Acks"/>,
/// so the phone stops sending them. Use from the UI thread.
/// </summary>
public sealed class SyncActionsService
{
    private const string DocumentKey = "sync.actions";
    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(14);
    private static readonly TimeSpan StepAwayFresh = TimeSpan.FromMinutes(10);

    private readonly SqliteJsonDocumentStore _store;
    private readonly ReceivedDevicesService _received;
    private readonly SyncIdentityService _identity;
    private readonly SessionTasksService _tasks;
    private readonly ComebackStore _comebacks;
    private readonly StepAwayService _stepAway;
    private readonly TimeProvider _time;
    private List<AppliedAction> _applied = [];
    private bool _applying;

    public SyncActionsService(
        SqliteJsonDocumentStore store, ReceivedDevicesService received, SyncIdentityService identity, SessionTasksService tasks,
        ComebackStore comebacks, StepAwayService stepAway, TimeProvider time)
    {
        _store = store;
        _received = received;
        _identity = identity;
        _tasks = tasks;
        _comebacks = comebacks;
        _stepAway = stepAway;
        _time = time;
    }

    /// <summary>Raised after actions were applied: this PC's file is written again with the new acks.</summary>
    public event EventHandler? Changed;

    /// <summary>The ids applied in the last 14 days, for this PC's sync file.</summary>
    public IReadOnlyList<Guid> Acks => _applied.Select(action => action.Id).ToList();

    public void LoadBlocking()
    {
        try
        {
            _applied = Task.Run(() => _store.LoadAsync<List<AppliedAction>>(DocumentKey)).GetAwaiter().GetResult() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
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
            Guid self = _identity.DeviceId;
            DateTimeOffset now = _time.GetUtcNow();
            var done = _applied.Select(action => action.Id).ToHashSet();
            List<SyncAction> fresh = _received.Devices
                .SelectMany(device => device.File.Actions ?? [])
                .Where(action => action.Target == self && !done.Contains(action.Id))
                .OrderBy(action => action.At)
                .ToList();
            if (fresh.Count == 0)
            {
                return;
            }

            foreach (SyncAction action in fresh)
            {
                await ApplyOneAsync(action, now);
                _applied.Add(new AppliedAction(action.Id, now));
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

    private async Task ApplyOneAsync(SyncAction action, DateTimeOffset now)
    {
        switch (action.Kind)
        {
            case SyncAction.KindTickTask when action.Item is Guid item && IsOpen(item):
                await _tasks.ToggleAsync(item);
                break;
            case SyncAction.KindPark when !string.IsNullOrWhiteSpace(action.Text):
                await _comebacks.AddAsync(action.Text.Trim());
                break;
            case SyncAction.KindTickComeback when action.Item is Guid comeback:
                await _comebacks.TickAsync(comeback);
                break;
            case SyncAction.KindStepAway when now - action.At < StepAwayFresh && !_stepAway.IsAway:
                _stepAway.StepAway(AwayKind.Break);
                break;
            case SyncAction.KindBack when now - action.At < StepAwayFresh && _stepAway.IsAway:
                _stepAway.Return();
                break;
        }
    }

    // A task or subtask of the current checklist that isn't ticked yet (ticking twice would untick it).
    private bool IsOpen(Guid id) => _tasks.Current.Tasks
        .SelectMany(task => task.Subtasks.Select(subtask => (subtask.Id, subtask.IsDone)).Prepend((task.Id, task.IsDone)))
        .Any(entry => entry.Id == id && !entry.IsDone);

    private sealed record AppliedAction(Guid Id, DateTimeOffset At);
}
