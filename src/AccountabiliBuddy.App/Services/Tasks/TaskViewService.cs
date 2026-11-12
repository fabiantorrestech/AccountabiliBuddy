using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// The Shelf's views (ADR 76, spec 05 section 10): the synced list of saved views (Default, Quick wins, Deep work and the user's),
/// the one in use with the changes made to it since (<see cref="Working"/>, not saved until <see cref="SaveAsync"/>), its search words
/// (this PC only) and what it shows (<see cref="Query"/>, <see cref="TaskViewRules"/>). The view the Shelf opens with is the account's
/// setting, the same on every device (<see cref="TaskAccountSettingsService.DefaultViewId"/>). The old filter screen's saved filters become views too, also the ones saved after the
/// migration (<see cref="MirrorPresetsAsync"/>). Use from the UI thread only.
/// </summary>
public sealed class TaskViewService
{
    private readonly TaskStore _store;
    private readonly TaskSettingsService _settings;
    private readonly TaskAccountSettingsService _account;
    private readonly TimeProvider _time;
    private HashSet<Guid> _mirrored = [];
    private Task _mirroring = Task.CompletedTask;

    public TaskViewService(TaskStore store, TaskSettingsService settings, TaskAccountSettingsService account, TimeProvider time)
    {
        _store = store;
        _settings = settings;
        _account = account;
        _time = time;
        Working = TaskViewConfig.Everything;
        Search = string.Empty;
        ActiveId = TaskView.DefaultId;
    }

    /// <summary>The view in use.</summary>
    public Guid ActiveId { get; private set; }

    /// <summary>What the Shelf shows now: the view's settings plus whatever was changed since.</summary>
    public TaskViewConfig Working { get; private set; }

    /// <summary>The search words now (private, never synced).</summary>
    public string Search { get; private set; }

    /// <summary>Raised when the views, the one in use or its changes differ.</summary>
    public event EventHandler? Changed;

    /// <summary>The live views in their order.</summary>
    public IReadOnlyList<TaskView> Views => TaskViewEdits.Live(_store.Lists);

    public TaskView? Active => Views.FirstOrDefault(view => view.Id == ActiveId);

    /// <summary>The view the Shelf opens with on every device: the account's pick when it still exists, else the built-in Default.</summary>
    public Guid DefaultViewId => _account.DefaultViewId is Guid id && Views.Any(view => view.Id == id) ? id : TaskView.DefaultId;

    /// <summary>Something differs from the saved view (Save is faded until it does).</summary>
    public bool IsChanged => Active is TaskView view
        && (TaskViewRules.Signature(Working) != TaskViewRules.Signature(view.Config) || Search != SavedSearch(view.Id));

    /// <summary>
    /// Makes sure the built-in views exist and opens the default view (<see cref="TaskAccountSettingsService.DefaultViewId"/>, else Default). Called
    /// once the task list is loaded.
    /// </summary>
    public async Task StartAsync()
    {
        await _store.ChangeAsync(this, (lists, _, now) => TaskViewEdits.WithBuiltIns(lists, now));
        Guid wanted = _account.DefaultViewId is Guid id && Views.Any(view => view.Id == id) ? id : TaskView.DefaultId;
        Open(wanted);
    }

    /// <summary>Opens a view, dropping the changes made to the one before; an unknown id changes nothing.</summary>
    public void Select(Guid id)
    {
        if (Views.Any(view => view.Id == id))
        {
            Open(id);
        }
    }

    /// <summary>Changes what the Shelf shows (a filter, the grouping, the sort, density, pinning) without saving the view.</summary>
    public void Modify(Func<TaskViewConfig, TaskViewConfig> change)
    {
        Working = change(Working);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSearch(string text)
    {
        Search = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back to everything, grouped by bins, manual order, pinning as the Tasks setting says (the red "reset" of the View menu).</summary>
    public void Reset()
    {
        Working = TaskViewConfig.Everything with { PinHighlights = _settings.Current.PinHighlights };
        Search = string.Empty;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the changes into the view in use (its search words stay on this PC).</summary>
    public async Task SaveAsync()
    {
        TaskViewConfig config = Working;
        string search = Search;
        Guid id = ActiveId;
        await _store.ChangeAsync(this, (lists, _, now) => TaskViewEdits.Update(lists, id, null, null, config, now));
        await _store.SetViewSearchAsync(id, search);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps what the Shelf shows as a new view under a name and opens it; null when the list is full.</summary>
    public async Task<TaskView?> SaveAsAsync(string name, string? icon = null)
    {
        TaskViewConfig config = Working;
        string search = Search;
        TaskView? saved = null;
        await _store.ChangeAsync(this, (lists, _, now) =>
        {
            (TaskLists after, saved) = TaskViewEdits.Save(lists, name, icon, config, now);
            return after;
        });
        if (saved is null)
        {
            return null;
        }

        await _store.SetViewSearchAsync(saved.Id, search);
        Open(saved.Id);
        return saved;
    }

    public Task RenameAsync(Guid id, string name, string? icon = null)
    {
        return _store.ChangeAsync(this, (lists, _, now) => TaskViewEdits.Update(lists, id, name, icon, null, now));
    }

    /// <summary>Deletes a saved view (never a built-in one). When it was in use, the default view opens.</summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        bool deleted = await _store.ChangeAsync(this, (lists, _, now) => TaskViewEdits.Delete(lists, id, now));
        if (!deleted)
        {
            return false;
        }

        await _store.SetViewSearchAsync(id, null);
        if (_account.DefaultViewId == id)
        {
            await _account.SetDefaultViewAsync(null);
        }

        if (ActiveId == id)
        {
            Open(TaskView.DefaultId);
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>The view the Shelf opens with on every device (null: the built-in Default).</summary>
    public Task SetDefaultAsync(Guid? id)
    {
        return _account.SetDefaultViewAsync(id);
    }

    /// <summary>
    /// Shuffle's reshuffle: a new order, stored with the view in use (and synced) until the next one. Needs the view to sort by shuffle
    /// (it is set when it doesn't). Today is never part of it: only the tasks <see cref="Query"/> is asked for.
    /// </summary>
    public async Task ReshuffleAsync()
    {
        int seed = Random.Shared.Next(1, int.MaxValue);
        Working = Working with { Sort = TaskViewConfig.SortShuffle, Seed = seed };
        Guid id = ActiveId;
        await _store.ChangeAsync(this, (lists, _, now) => TaskViewEdits.Reshuffled(lists, id, seed, now));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the view shows now: the live tasks of <paramref name="scope"/> (the Shelf's bins; null for all), filtered, grouped and sorted.</summary>
    public TaskViewResult Query(IReadOnlyList<TaskBin>? scope = null, bool showEmptyBins = false)
    {
        TaskLists lists = _store.Lists;
        var context = new TaskViewContext(
            _time.GetUtcNow(), TimeZoneInfo.Local, lists.Labels.Select(item => item.Label).ToList(), Search, scope, showEmptyBins);
        return TaskViewRules.Apply(lists.Tasks.Select(item => item.Task), Working, context);
    }

    /// <summary>The tasks and steps a search finds (a number like "142b", or words), whatever the view; for the search box's own list.</summary>
    public IReadOnlyList<TaskSearchHit> Find(string query)
    {
        return TaskSearch.Find(_store.Lists.Tasks.Select(item => item.Task), query);
    }

    /// <summary>
    /// The PC's saved filters (<paramref name="presets"/>) as views, once each: a filter not mirrored before gets a view (its search words
    /// stay on this PC); one that was is never updated or recreated, so what the new screens changed or deleted stays. A filter gone from the
    /// old screen deletes its view. Calls run one after another.
    /// </summary>
    public Task MirrorPresetsAsync(IReadOnlyList<TaskFilterPreset> presets)
    {
        _mirroring = MirrorAfterAsync(_mirroring, presets);
        return _mirroring;
    }

    // After the mirror before it, on the UI thread: the task list, its Changed event and every view of it belong to the UI thread.
    private async Task MirrorAfterAsync(Task previous, IReadOnlyList<TaskFilterPreset> presets)
    {
        await previous;
        await Dispatcher.UIThread.InvokeAsync(() => MirrorAsync(presets));
    }

    private async Task MirrorAsync(IReadOnlyList<TaskFilterPreset> presets)
    {
        try
        {
            if (!_store.IsActive)
            {
                return;
            }

            IReadOnlyDictionary<Guid, string> categoryNames = _store.Categories.GroupBy(category => category.Id).ToDictionary(group => group.Key, group => group.First().Name);
            HashSet<Guid> gone = _mirrored.Except(presets.Select(preset => preset.Id)).ToHashSet();
            HashSet<Guid> toAdd = TaskViewEdits.PresetsToMirror(presets.Select(preset => preset.Id), _mirrored, _store.Lists).ToHashSet();
            await _store.ChangeAsync(this, (lists, _, now) =>
            {
                var labels = lists.Labels.Select(item => item.Label).Where(label => label.IsLive).ToList();
                foreach (TaskFilterPreset preset in presets.Where(preset => toAdd.Contains(preset.Id)))
                {
                    int unmapped = 0;
                    var filter = new LegacyTaskFilter(
                        preset.Id, preset.Name, preset.View.Statuses, preset.View.CategoryIds, preset.View.NoCategory, preset.View.Due, preset.View.Sort, preset.Search);
                    TaskViewConfig config = LegacyTaskFilterMapper.Config(
                        filter, categoryNames, name => labels.FirstOrDefault(label => TaskLabel.SameName(label.Name, name))?.Id, ref unmapped);
                    lists = TaskViewEdits.Upsert(lists, preset.Id, preset.Name, config, now);
                }

                foreach (Guid id in gone)
                {
                    lists = TaskViewEdits.Delete(lists, id, now);
                }

                return lists;
            });
            foreach (TaskFilterPreset preset in presets.Where(preset => toAdd.Contains(preset.Id)))
            {
                await _store.SetViewSearchAsync(preset.Id, preset.Search);
            }

            foreach (Guid id in gone)
            {
                await _store.SetViewSearchAsync(id, null);
            }

            _mirrored = presets.Select(preset => preset.Id).ToHashSet();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Kind of failure only: the filters hold the user's text. The next save tries again.
            System.Diagnostics.Trace.TraceError("Mirroring saved filters to views failed: " + ex.GetType().Name);
        }
    }

    /// <summary>Keeps the old filter screen's saved filters in step with the views from now on (and gives the ones made since the migration a view).</summary>
    public void FollowPresets(TaskFilterPresetStore presets)
    {
        IReadOnlyList<TaskFilterPreset> held = presets.Load();
        // Filters that already have a view (live or deleted) were mirrored before: they are left alone from here on.
        _mirrored = held.Select(preset => preset.Id).Where(id => _store.Lists.Views.Any(item => item.Id == id)).ToHashSet();
        _ = MirrorPresetsAsync(held);
        presets.Saved += list => _ = MirrorPresetsAsync(list);
    }

    private void Open(Guid id)
    {
        ActiveId = id;
        TaskView? view = Active;
        Working = view?.Config ?? TaskViewConfig.Everything;
        Search = SavedSearch(id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string SavedSearch(Guid id)
    {
        return _store.ViewSearches.TryGetValue(id, out string? search) ? search : string.Empty;
    }
}
