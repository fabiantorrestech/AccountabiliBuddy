using AccountabiliBuddy.App.Services.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One line of the unreadable and leftover files list: the method, whose it is (a device number, never a name), when and why; the actions it allows.</summary>
public sealed class SyncUnreadableRowViewModel
{
    public SyncUnreadableRowViewModel(SyncUnreadableFile file, IReadOnlyDictionary<Guid, string> ordinals, DateTimeOffset now, TimeZoneInfo zone, bool canRemoveStale)
    {
        File = file;
        string who = file.Device is Guid id && ordinals.TryGetValue(id, out string? ordinal) ? ordinal : file.OwnStale ? "This PC" : "A device";
        Title = $"{SyncBoardBuilder.TitleOf(file.Method)} · {who}";
        Detail = $"{SyncBoardWords.Clock(file.At, now, zone)} · {SyncBoardWords.ReasonOfFile(file.Reason)}";
        IsProblem = file.IsProblem;
        CanRemoveStale = canRemoveStale && file.OwnStale && file.Method is "folder" or "drive";
        CanTryAgain = file.Method is "folder" or "drive" or "relay";
    }

    /// <summary>The entry the actions act on.</summary>
    public SyncUnreadableFile File { get; }

    public string Title { get; }

    public string Detail { get; }

    /// <summary>A real problem (damaged, too big, a newer app); false for an old group's leftover or an extra copy of this PC's own file.</summary>
    public bool IsProblem { get; }

    /// <summary>"Remove my stale file": this PC's own old-group or extra copy, on the folder or Drive only (never the relay).</summary>
    public bool CanRemoveStale { get; }

    public bool CanTryAgain { get; }
}
