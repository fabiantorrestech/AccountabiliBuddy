using AccountabiliBuddy.App.Services.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One task "Changed on both devices" in the conflicts list. The title is the user's own text, shown here only.</summary>
public sealed class SyncConflictRowViewModel
{
    public SyncConflictRowViewModel(SyncConflictItem item)
    {
        Id = item.Id;
        Title = item.Title.Length > 0 ? item.Title : "(no title)";
        Detail = item.Count == 1 ? "1 change waits for your answer" : $"{item.Count} changes wait for your answer";
    }

    public Guid Id { get; }

    public string Title { get; }

    public string Detail { get; }
}
