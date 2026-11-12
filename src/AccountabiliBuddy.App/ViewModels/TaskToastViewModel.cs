using System.Diagnostics;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Tasks pages' toast (spec 03 §F, 05 §14): one at a time (a new one replaces the old), bottom-left of the Tasks card and of Today's
/// sections, with Undo when the action can be taken back, a × and a timer bar that shrinks over its life (4 s plain, 6 s with Undo,
/// 7 s for an explanation); the pointer resting on it holds the timer. It shows every task action's toast
/// (<see cref="TaskActionsService.Toast"/>), the task list's notices (<see cref="TaskStore.Notice"/>), a delete from the older lists
/// (Recently deleted, whose Undo restores that entry), a row's tick ("Finished …" at once, Undo takes the tick back) and the page's own
/// lines ("Copied 142 · …"). Ctrl+Z (<see cref="UndoLatestAsync"/>) is the shown toast's Undo, else the task actions' undo stack (the
/// last 10, also after the toast is gone). The timer is a plain stopwatch: a toast's life is not a rule anything relies on. The text
/// holds the user's titles: shown, never logged. Use from the UI thread only.
/// </summary>
public sealed partial class TaskToastViewModel : ObservableObject, IDisposable
{
    // The bar moves this often (its 0.1 s transition smooths the steps).
    private static readonly TimeSpan TickTime = TimeSpan.FromMilliseconds(100);

    private readonly SessionTasksService _tasks;
    private readonly RecentlyDeletedStore? _deleted;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private TaskActionsService? _actions;
    private TaskStore? _store;
    private TaskToastClock _clock;
    private Func<Task>? _undo;
    private Guid? _deletedEntry;
    private bool _isHovered;

    public TaskToastViewModel(SessionTasksService tasks)
    {
        _tasks = tasks;
        _deleted = tasks.Deleted;
        Text = string.Empty;
        _timer = new DispatcherTimer { Interval = TickTime };
        _timer.Tick += (_, _) => OnTick();
        if (_deleted is not null)
        {
            _deleted.Recorded += OnDeleteRecorded;
            _deleted.Changed += OnDeletedChanged;
        }
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool HasUndo { get; set; }

    /// <summary>The timer bar's share of the toast's width: 1 when it appears, 0 when it goes.</summary>
    [ObservableProperty]
    public partial double Left { get; set; }

    /// <summary>The task actions and the task list are the source (ADR 76): their toasts and notices show here from now on.</summary>
    public void Use(TaskActionsService actions, TaskStore store)
    {
        if (_actions is not null)
        {
            return;
        }

        _actions = actions;
        _store = store;
        actions.Toast += OnActionToast;
        store.Notice += OnStoreNotice;
    }

    /// <summary>A plain line (4 seconds), e.g. "Copied 142 · Draft Q4…".</summary>
    public void Say(string text)
    {
        Show(text, TaskToastLength.Plain, undo: null);
    }

    /// <summary>
    /// Shows <paramref name="text"/> for <paramref name="length"/>, replacing whatever shows; <paramref name="undo"/> (when given) is
    /// what Undo and Ctrl+Z do while it shows.
    /// </summary>
    public void Show(string text, TaskToastLength length, Func<Task>? undo)
    {
        _deletedEntry = null;
        _undo = undo;
        Text = text;
        HasUndo = undo is not null;
        _clock = TaskToastClock.Start(TaskToastTiming.For(length), Now);
        if (_isHovered)
        {
            _clock = _clock.Pause(Now);
        }

        Left = 1;
        IsVisible = true;
        _timer.Start();
    }

    /// <summary>
    /// Ctrl+Z: the shown toast's Undo when it has one; otherwise the newest action on the task actions' undo stack (the last 10, after
    /// its toast is gone too). False when there was nothing to undo.
    /// </summary>
    public async Task<bool> UndoLatestAsync()
    {
        if (IsVisible && _undo is { } undo)
        {
            await RunUndoAsync(undo);
            return true;
        }

        if (_actions is { CanUndo: true } actions)
        {
            await actions.UndoAsync();
            return true;
        }

        return false;
    }

    /// <summary>The pointer is on the toast: its timer holds until it leaves.</summary>
    public void SetHovered(bool hovered)
    {
        if (_isHovered == hovered)
        {
            return;
        }

        _isHovered = hovered;
        _clock = hovered ? _clock.Pause(Now) : _clock.Resume(Now);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_deleted is not null)
        {
            _deleted.Recorded -= OnDeleteRecorded;
            _deleted.Changed -= OnDeletedChanged;
        }

        if (_actions is not null)
        {
            _actions.Toast -= OnActionToast;
        }

        if (_store is not null)
        {
            _store.Notice -= OnStoreNotice;
        }
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (_undo is { } undo)
        {
            await RunUndoAsync(undo);
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        Hide();
    }

    private TimeSpan Now => _watch.Elapsed;

    // The undo runs once: its toast goes (an undo raises its own "Undone" toast, or says why it couldn't).
    private async Task RunUndoAsync(Func<Task> undo)
    {
        Hide();
        await undo();
    }

    private void OnActionToast(object? sender, TaskToastEventArgs e)
    {
        // The stored finish of a row's tick says what its tick already says: the toast showing (and its Undo) stays as it is.
        if (IsVisible && e.Text == Text)
        {
            return;
        }

        TaskActionsService? actions = _actions;
        Show(e.Text, TaskToastTiming.LengthOf(e.CanUndo, e.Explains), e.CanUndo && actions is not null ? () => actions.UndoAsync() : null);
    }

    private void OnStoreNotice(object? sender, TaskToastEventArgs e)
    {
        Show(e.Text, TaskToastTiming.LengthOf(canUndo: false, e.Explains), undo: null);
    }

    // A delete from the older lists (a running session's task, a Later item, a come-back): Undo restores that entry of Recently deleted.
    private void OnDeleteRecorded(object? sender, DeletedItem item)
    {
        Guid entryId = item.EntryId;
        Show("Deleted “" + TaskToasts.Short(item.Title) + "”", TaskToastLength.WithUndo, () => RestoreAsync(entryId));
        _deletedEntry = entryId;
    }

    // Restored from Recently deleted (or by another toast): the toast offering it goes too.
    private void OnDeletedChanged(object? sender, EventArgs e)
    {
        if (_deletedEntry is Guid id && _deleted?.Find(id) is null)
        {
            Hide();
        }
    }

    // What Undo can't do (a 4th task in a full session) it says, as an explanation.
    private async Task RestoreAsync(Guid entryId)
    {
        (bool restored, string? message) = await _tasks.RestoreAsync(entryId);
        if (!restored && !string.IsNullOrEmpty(message))
        {
            Show(message, TaskToastLength.Explanation, undo: null);
        }
    }

    private void OnTick()
    {
        TimeSpan now = Now;
        if (_clock.IsOver(now))
        {
            Hide();
            return;
        }

        Left = _clock.Left(now);
    }

    private void Hide()
    {
        _timer.Stop();
        IsVisible = false;
        HasUndo = false;
        _undo = null;
        _deletedEntry = null;
        Left = 0;
    }
}
