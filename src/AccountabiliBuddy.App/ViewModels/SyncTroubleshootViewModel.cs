using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Settings › Devices &amp; sync › Troubleshoot sync (SYNC1b, ADR 86), always shown at the bottom of the page, in both modes: the confirmation board
/// (per method, per other device, per item, with a dot, an icon and a sentence), the files that aren't taken in (Ignore, Try again, Remove my stale
/// file), "Re-read everything", "Send my files again now", "Re-read goal history (full)", the conflicts list, the sync log's export, and the
/// "For testing" receipts switch. It is display and maintenance only: nothing here is locked in a focus session (merges keep their own holds,
/// nothing applies a looser setting) except answering a day plan clash, which is a Day plan change and is view-only then. With no sync method on
/// it says "Not syncing" and shows no red. Use from the UI thread.
/// </summary>
public sealed partial class SyncTroubleshootViewModel : ObservableObject, IDisposable
{
    /// <summary>What goals say about conflicts (they have none by design).</summary>
    public const string GoalsNote = "Goals merge per field; the newest edit wins, so they have no conflicts to answer.";

    /// <summary>Why the day plan question can't be answered in a session.</summary>
    public const string ClashLockedText = "Answer after the session";

    private readonly SyncTroubleshootService _service;
    private readonly SyncCoordinator _coordinator;
    private readonly ReceivedDevicesService _received;
    private readonly SessionController _sessions;
    private readonly TimeProvider _time;
    private readonly ISyncConflictSource? _conflicts;
    private readonly HashSet<(string Method, Guid Device)> _open = [];
    private bool _disposed;

    public SyncTroubleshootViewModel(
        SyncTroubleshootService service, SyncCoordinator coordinator, ReceivedDevicesService received, SessionController sessions, TimeProvider time,
        ISyncConflictSource? conflicts)
    {
        _service = service;
        _coordinator = coordinator;
        _received = received;
        _sessions = sessions;
        _time = time;
        _conflicts = conflicts;
        ResultText = string.Empty;
        NewestText = string.Empty;
        ClashText = string.Empty;
        received.Changed += OnSourceChanged;
        if (coordinator.Unreadable is { } unreadable)
        {
            unreadable.Changed += OnSourceChanged;
        }

        if (coordinator.Receipts is { } receipts)
        {
            receipts.Changed += OnSourceChanged;
        }

        if (conflicts is not null)
        {
            conflicts.Changed += OnSourceChanged;
        }

        coordinator.ClashRaised += OnClashRaised;
        sessions.StateChanged += OnSessionChanged;
        Refresh();
    }

    /// <summary>Opens the resolve dialog for one task (newer first, keep this one, keep the other, keep both); the page shows it.</summary>
    public event EventHandler<ConflictDialogViewModel>? ConflictRequested;

    /// <summary>Asks before "Remove my stale file" (Remove / Cancel); the page shows it. Nothing is removed unless something shows it and runs it.</summary>
    public event EventHandler<GoalConfirmRequest>? ConfirmRequested;

    /// <summary>For testing: the receipts switch (<see cref="SyncReceiptsViewModel"/>), moved here from "For testing" (its key is unchanged). Set by the app.</summary>
    public SyncReceiptsViewModel? Receipts { get; init; }

    /// <summary>Shows the day plan clash dialog (the app's own, the one a fresh clash opens); set by the app.</summary>
    public Action<DayPlanClashRequest>? AnswerClash { get; set; }

    /// <summary>The methods in use, each with a row for every other device.</summary>
    public ObservableCollection<SyncBoardMethodViewModel> Methods { get; } = [];

    /// <summary>Files the methods hold that this PC doesn't take in (the shown ones; "Ignore" hides one).</summary>
    public ObservableCollection<SyncUnreadableRowViewModel> Unreadable { get; } = [];

    /// <summary>Tasks "Changed on both devices".</summary>
    public ObservableCollection<SyncConflictRowViewModel> TaskConflicts { get; } = [];

    /// <summary>No sync method is on: "Not syncing", no red anywhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBoard))]
    public partial bool IsNotSyncing { get; set; }

    /// <summary>Methods are on but this PC is alone in its group.</summary>
    [ObservableProperty]
    public partial bool IsAlone { get; set; }

    public bool HasBoard => !IsNotSyncing;

    /// <summary>"Your newest file: 4:05 PM", or empty.</summary>
    [ObservableProperty]
    public partial string NewestText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoUnreadable))]
    public partial bool HasUnreadable { get; set; }

    public bool HasNoUnreadable => !HasUnreadable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTaskConflicts))]
    [NotifyPropertyChangedFor(nameof(HasAnyConflict))]
    public partial bool HasTaskConflicts { get; set; }

    public bool HasNoTaskConflicts => !HasTaskConflicts;

    /// <summary>Something waits for an answer: a task changed on two devices or the day plan.</summary>
    public bool HasAnyConflict => HasTaskConflicts || HasClash;

    /// <summary>A day plan clash waits for an answer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyConflict))]
    public partial bool HasClash { get; set; }

    /// <summary>"Day plan changed here and on another device" or the locked text.</summary>
    [ObservableProperty]
    public partial string ClashText { get; set; }

    /// <summary>In a focus session: the clash row is view-only ("Answer after the session").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnswerClash))]
    public partial bool IsClashLocked { get; set; }

    public bool CanAnswerClash => HasClash && !IsClashLocked;

    /// <summary>The goal history button: "Wait a few minutes" while the relay's 5-minute gate for forced history passes is shut.</summary>
    [ObservableProperty]
    public partial string RereadHistoryText { get; set; } = "Re-read goal history";

    /// <summary>One line of counts after an action ("Re-read 4 files · 0 problems").</summary>
    [ObservableProperty]
    public partial string ResultText { get; set; }

    /// <summary>An action runs: the buttons rest.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    /// <summary>A name for the report's file.</summary>
    public string SuggestedReportName => $"sync-diagnostics-{_time.GetLocalNow():yyyy-MM-dd}.txt";

    /// <summary>Rebuilds the board and the lists from what is known now.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        TimeZoneInfo zone = _time.LocalTimeZone;
        SyncBoard board = _service.BuildBoard();
        IsNotSyncing = board.NotSyncing;
        IsAlone = board.NoOtherDevices;
        NewestText = board.Newest is { } newest ? $"Your newest file went out at {SyncBoardWords.Clock(newest, now, zone)}" : "Your newest file hasn't gone out yet";
        CollectOpenRows();
        Methods.Clear();
        foreach (SyncBoardMethod method in board.Methods)
        {
            Methods.Add(new SyncBoardMethodViewModel(method, now, zone, _open));
        }

        IReadOnlyDictionary<Guid, string> ordinals = _service.Ordinals();
        RereadHistoryText = _service.RelayHistoryOpen ? "Re-read goal history" : "Wait a few minutes";
        Unreadable.Clear();
        foreach (SyncUnreadableFile file in _service.Files?.Shown ?? [])
        {
            Unreadable.Add(new SyncUnreadableRowViewModel(file, ordinals, now, zone, canRemoveStale: true));
        }

        HasUnreadable = Unreadable.Count > 0;
        TaskConflicts.Clear();
        foreach (SyncConflictItem item in _conflicts?.List() ?? [])
        {
            TaskConflicts.Add(new SyncConflictRowViewModel(item));
        }

        HasTaskConflicts = TaskConflicts.Count > 0;
        RefreshClash();
    }

    // The rows the user opened stay open across a refresh.
    private void CollectOpenRows()
    {
        foreach (SyncBoardMethodViewModel method in Methods)
        {
            foreach (SyncBoardDeviceViewModel device in method.Devices)
            {
                if (device.IsExpanded)
                {
                    _open.Add((method.Method, device.Key));
                }
                else
                {
                    _open.Remove((method.Method, device.Key));
                }
            }
        }
    }

    private void RefreshClash()
    {
        HasClash = _coordinator.PendingClash is not null;
        IsClashLocked = _sessions.IsEnforcing;
        ClashText = !HasClash
            ? string.Empty
            : IsClashLocked
                ? $"The day plan was changed here and on {_coordinator.PendingClash!.FromDevice}. {ClashLockedText}."
                : $"The day plan was changed here and on {_coordinator.PendingClash!.FromDevice}.";
        OnPropertyChanged(nameof(CanAnswerClash));
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void OnClashRaised(object? sender, DayPlanClashRequest request) => Dispatcher.UIThread.Post(RefreshClash);

    private void OnSessionChanged(object? sender, SessionState state) => Dispatcher.UIThread.Post(RefreshClash);

    [RelayCommand]
    private Task RereadAllAsync() => RunAsync(() => _service.RereadAllAsync());

    [RelayCommand]
    private Task SendAgainAsync() => RunAsync(_service.SendAgainAsync);

    [RelayCommand]
    private Task RereadHistoryAsync() => RunAsync(_service.RereadHistoryAsync);

    [RelayCommand]
    private async Task TryAgainAsync(SyncUnreadableRowViewModel? row)
    {
        if (row is not null)
        {
            await RunAsync(() => _service.TryAgainAsync(row.File));
        }
    }

    [RelayCommand]
    private void Ignore(SyncUnreadableRowViewModel? row)
    {
        if (row is not null)
        {
            _service.Ignore(row.File);
        }
    }

    // Asks first: the dialog names the method and "this PC's old copy"; only its Remove button runs the removal.
    [RelayCommand]
    private void RemoveStale(SyncUnreadableRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        string where = SyncBoardBuilder.TitleOf(row.File.Method);
        ConfirmRequested?.Invoke(this, new GoalConfirmRequest(
            "Remove this PC's old copy?",
            $"This deletes this PC's own old copy of its sync file from {where}. Other devices don't use it and this PC's current file stays. It can't be brought back from here.",
            "Remove",
            IsDanger: true,
            CanRun: true,
            async () =>
            {
                await RunAsync(() => _service.RemoveStaleAsync(row.File));
                return null;
            },
            "Sync"));
    }

    [RelayCommand]
    private void OpenConflict(SyncConflictRowViewModel? row)
    {
        if (row is not null && _conflicts?.DialogFor(row.Id) is { } dialog)
        {
            ConflictRequested?.Invoke(this, dialog);
        }
    }

    [RelayCommand]
    private void ShowClash()
    {
        // View-only in a focus session: the day plan is locked then, and the answer is a Day plan change.
        if (CanAnswerClash && _coordinator.PendingClash is { } request)
        {
            AnswerClash?.Invoke(request);
        }
    }

    /// <summary>Saves the report where the user chose (never inside the sync folder); the line to show.</summary>
    public async Task<string> ExportToAsync(string path)
    {
        string line = await _service.ExportAsync(path);
        ResultText = line;
        return line;
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ResultText = "Working…";
        try
        {
            ResultText = await action();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A store or a network call that failed unexpectedly (a locked database, a closed store): one fixed line, no detail, never a crash.
            ResultText = "Couldn't finish · try again";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _received.Changed -= OnSourceChanged;
        if (_coordinator.Unreadable is { } unreadable)
        {
            unreadable.Changed -= OnSourceChanged;
        }

        if (_coordinator.Receipts is { } receipts)
        {
            receipts.Changed -= OnSourceChanged;
        }

        if (_conflicts is not null)
        {
            _conflicts.Changed -= OnSourceChanged;
        }

        _coordinator.ClashRaised -= OnClashRaised;
        _sessions.StateChanged -= OnSessionChanged;
    }
}
