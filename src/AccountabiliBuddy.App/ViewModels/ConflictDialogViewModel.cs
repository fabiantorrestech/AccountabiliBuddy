using System.Globalization;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Changed on both devices" (P9, spec 05 §11): the task's title or note was changed on two devices since they last agreed
/// (<see cref="TaskConflict"/>, made by the field-level merge). Both versions are listed, the newer first and highlighted; a click keeps
/// that one (<see cref="TaskActionsService.ResolveConflictAsync"/>, stamped so the conflict closes on every device once synced). "Keep
/// both as separate tasks" puts the other version in Queued; "Decide later" changes nothing. A task with two conflicts (title and
/// note) shows them one after the other. Holds the user's text: shown only, never logged. Use from the UI thread.
/// </summary>
public sealed partial class ConflictDialogViewModel : ObservableObject
{
    public const string Foot = "Steps, tags and reminders changed on only one device are kept either way.";

    private readonly TaskStore _store;
    private readonly TaskActionsService _actions;
    private readonly Func<Guid, (string Name, string Kind)?> _deviceOf;
    private readonly Guid? _self;
    private readonly TimeProvider _time;
    private readonly Guid _taskId;
    private TaskConflict? _conflict;
    private string? _shownText;
    private int _picked;

    public ConflictDialogViewModel(
        TaskStore store, TaskActionsService actions, Guid taskId, Func<Guid, (string Name, string Kind)?> deviceOf, Guid? self, TimeProvider time)
    {
        _store = store;
        _actions = actions;
        _taskId = taskId;
        _deviceOf = deviceOf;
        _self = self;
        _time = time;
        TaskTitle = Head = CountText = string.Empty;
        Load();
        _store.Changed += OnStoreChanged;
    }

    /// <summary>Asks the dialog to close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The sub line: the task's title as shown.</summary>
    [ObservableProperty]
    public partial string TaskTitle { get; set; }

    /// <summary>"Keep one version" (of the title) or "Keep one version of the note".</summary>
    [ObservableProperty]
    public partial string Head { get; set; }

    /// <summary>"1 of 2" when the title and the note both wait ("2 of 2" after the first pick); empty for one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    public partial string CountText { get; set; }

    public bool HasCount => CountText.Length > 0;

    /// <summary>The two versions, the newer first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ConflictVersionViewModel> Versions { get; set; } = [];

    /// <summary>The task left the list or the conflict closed (picked on another device) while the dialog was open.</summary>
    [ObservableProperty]
    public partial bool IsGone { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KeepBothCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>The dialog closed: stop following the task list.</summary>
    public void Detach()
    {
        _store.Changed -= OnStoreChanged;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task KeepBothAsync()
    {
        return ActAsync(conflict => _actions.KeepBothVersionsAsync(_taskId, conflict));
    }

    [RelayCommand]
    private void DecideLater()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAct() => !IsBusy && !IsGone;

    private void OnStoreChanged(object? sender, TaskStoreChangedEventArgs e)
    {
        if (!IsBusy)
        {
            Load();
        }
    }

    // The task's first open conflict (the title before the note), with the version shown now and the other one.
    private void Load()
    {
        SyncTaskItem? item = _store.Lists.Tasks.FirstOrDefault(candidate => candidate.Id == _taskId && candidate.Task.IsLive);
        List<TaskConflict> open = (item?.Task.Conflicts ?? [])
            .Where(candidate => candidate.Field is TaskConflict.TitleField or TaskConflict.NoteField)
            .OrderBy(candidate => candidate.Field == TaskConflict.TitleField ? 0 : 1)
            .ToList();
        TaskConflict? conflict = open.FirstOrDefault();
        if (item is null || conflict is null)
        {
            _conflict = null;
            _shownText = null;
            IsGone = true;
            Versions = [];
            KeepBothCommand.NotifyCanExecuteChanged();
            return;
        }

        bool isTitle = conflict.Field == TaskConflict.TitleField;
        TaskField field = isTitle ? TaskField.Title : TaskField.Note;
        string? shownText = TaskFieldValues.Text(item.Task, field);

        // A sync that changed the shown value (the conflict still open) rebuilds the cards too.
        bool sameAsBefore = _conflict == conflict && string.Equals(_shownText, shownText, StringComparison.Ordinal);
        _conflict = conflict;
        _shownText = shownText;
        IsGone = false;
        TaskTitle = item.Task.Title;
        Head = isTitle ? "Keep one version" : "Keep one version of the note";
        int total = _picked + open.Count;
        CountText = total > 1 ? (_picked + 1).ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture) : string.Empty;
        if (sameAsBefore && Versions.Count == 2)
        {
            return;
        }

        TaskFieldStamp? stamp = item.Stamps.Of(field);
        DateTimeOffset shownAt = stamp?.At ?? item.Task.EditedAt;
        bool shownIsNewer = shownAt >= conflict.OtherEditedAt;
        string what = isTitle ? "title" : "note";
        ConflictVersionViewModel shown = Version(shownText, stamp?.By ?? Guid.Empty, shownAt, what, shownIsNewer, keepOther: false);
        ConflictVersionViewModel other = Version(conflict.OtherValue, conflict.OtherDeviceId, conflict.OtherEditedAt, what, !shownIsNewer, keepOther: true);
        Versions = shownIsNewer ? [shown, other] : [other, shown];
        KeepBothCommand.NotifyCanExecuteChanged();
    }

    private ConflictVersionViewModel Version(string? text, Guid deviceId, DateTimeOffset at, string what, bool isNewer, bool keepOther)
    {
        (string name, MaterialIconKind icon) = DeviceOf(deviceId);
        bool empty = string.IsNullOrWhiteSpace(text);
        return new ConflictVersionViewModel(
            empty ? "(no note)" : text!.Trim(), name + " · " + what, TimeText(at), icon, isNewer, empty,
            new AsyncRelayCommand(() => ActAsync(conflict => _actions.ResolveConflictAsync(_taskId, conflict, keepOther)), CanAct));
    }

    private (string Name, MaterialIconKind Icon) DeviceOf(Guid deviceId)
    {
        if (deviceId == Guid.Empty)
        {
            return ("An earlier copy", MaterialIconKind.History);
        }

        if (deviceId == _self)
        {
            return ("This PC", MaterialIconKind.Laptop);
        }

        if (_deviceOf(deviceId) is not { } device)
        {
            return ("Another device", MaterialIconKind.Devices);
        }

        MaterialIconKind icon = device.Kind switch
        {
            SyncDeviceCard.KindPhone => MaterialIconKind.Cellphone,
            SyncDeviceCard.KindWatch => MaterialIconKind.WatchVariant,
            _ => MaterialIconKind.Laptop,
        };
        return (device.Name, icon);
    }

    // "10:44 AM" today, else "Tue 11/10 10:44 AM"; always 12-hour.
    private string TimeText(DateTimeOffset at)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).Date;
        string clock = local.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return local.Date == today ? clock : local.ToString("ddd M/d", CultureInfo.InvariantCulture) + " " + clock;
    }

    // One pick at a time; then the next conflict of the task, or the dialog closes.
    private async Task ActAsync(Func<TaskConflict, Task<bool>> act)
    {
        if (IsBusy || _conflict is not TaskConflict conflict)
        {
            return;
        }

        IsBusy = true;
        bool done;
        try
        {
            done = await act(conflict);
        }
        finally
        {
            IsBusy = false;
        }

        if (done)
        {
            _picked++;
        }

        _conflict = null;
        Load();
        if (IsGone)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
