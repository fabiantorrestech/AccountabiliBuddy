using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of Archive › Recently deleted: what it was, when it was deleted, and Restore (or delete it for good).</summary>
public sealed partial class RecentlyDeletedRowViewModel : ObservableObject
{
    private readonly Guid _entryId;
    private readonly SessionTasksService _tasks;

    public RecentlyDeletedRowViewModel(DeletedItem item, string whenText, SessionTasksService tasks)
    {
        _entryId = item.EntryId;
        _tasks = tasks;
        Title = item.Title;
        KindText = item.Kind switch
        {
            DeletedKind.Step or DeletedKind.HistoryStep => "Step",
            DeletedKind.Later => "Later",
            DeletedKind.ComeBack => "Come back to",
            _ => "Task",
        };
        WhenText = whenText;
        Message = string.Empty;
    }

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
        return _tasks.Deleted is null ? Task.CompletedTask : _tasks.Deleted.RemoveAsync(_entryId);
    }
}
