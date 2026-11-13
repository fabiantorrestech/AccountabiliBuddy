using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of Archive › Recently deleted: what it was, when it was deleted, and Restore (or delete it for good).</summary>
public sealed partial class RecentlyDeletedRowViewModel : ObservableObject
{
    private readonly Guid _entryId;
    private readonly SessionTasksService? _tasks;
    private readonly Func<Task<bool>>? _restore;

    public RecentlyDeletedRowViewModel(DeletedItem item, string whenText, SessionTasksService tasks)
    {
        _entryId = item.EntryId;
        _tasks = tasks;
        Title = item.Title;
        KindText = item.Kind switch
        {
            DeletedKind.Step or DeletedKind.HistoryStep or DeletedKind.LaterStep => "Step",
            DeletedKind.Later => "Later",
            DeletedKind.ComeBack => "Come back to",
            DeletedKind.AutoSaved => "Auto-saved",
            _ => "Task",
        };
        WhenText = whenText;
        Message = string.Empty;
    }

    /// <summary>
    /// A line every device can build (AR1-fix): a task deleted from the one task list (<paramref name="restore"/> brings it back through
    /// History) or a past task deleted anywhere (null: nothing here can bring it back, so no Restore).
    /// </summary>
    public RecentlyDeletedRowViewModel(ArchiveDeletedRow row, string whenText, Func<Task<bool>>? restore)
    {
        _entryId = row.EntryId ?? Guid.Empty;
        _restore = restore;
        Title = row.Title;
        KindText = "Task";
        WhenText = whenText;
        Message = string.Empty;
    }

    /// <summary>Whether Restore is offered (a line of another device's delete may have nothing to bring back).</summary>
    public bool CanRestore => _tasks is not null || _restore is not null;

    /// <summary>Whether "Delete for good" is offered: only the device's own Recently deleted lines can be erased here.</summary>
    public bool CanForget => _tasks is not null;

    public string Title { get; }

    /// <summary>"Task", "Step", "Later" or "Come back to".</summary>
    public string KindText { get; }

    /// <summary>E.g. "Deleted Thu 10/29 11:02 AM".</summary>
    public string WhenText { get; }

    /// <summary>Why Restore could not put it back, e.g. "This session already has 3 tasks."</summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    public bool HasMessage => Message.Length > 0;

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (_restore is not null)
        {
            if (!await _restore())
            {
                Message = "It can't be brought back now.";
            }

            return;
        }

        if (_tasks is null)
        {
            return;
        }

        (bool restored, string? message) = await _tasks.RestoreAsync(_entryId);
        if (!restored)
        {
            Message = message ?? string.Empty;
        }
    }

    /// <summary>Deletes it for good: its content is gone from this PC.</summary>
    [RelayCommand]
    private Task ForgetAsync()
    {
        return _tasks?.Deleted is null ? Task.CompletedTask : _tasks.Deleted.RemoveAsync(_entryId);
    }
}
