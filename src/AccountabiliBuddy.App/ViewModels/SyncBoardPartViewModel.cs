using AccountabiliBuddy.App.Services.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One item (part of the sync file) under a device on the board: its name and dot.</summary>
public sealed class SyncBoardPartViewModel
{
    public SyncBoardPartViewModel(SyncBoardPart part)
    {
        Name = SyncBoardWords.PartName(part.Code);
        Dot = new SyncDotViewModel(part.Dot);
    }

    public string Name { get; }

    public SyncDotViewModel Dot { get; }
}
