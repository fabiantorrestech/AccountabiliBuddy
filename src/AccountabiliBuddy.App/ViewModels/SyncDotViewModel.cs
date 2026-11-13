using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Sync;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A board dot as the screen shows it (SYNC1b): the colour as a class, never alone: an icon and a sentence go with it.</summary>
public sealed class SyncDotViewModel
{
    public SyncDotViewModel(SyncDot dot)
    {
        Icon = SyncBoardWords.IconOf(dot.Color);
        Words = SyncBoardWords.Words(dot);
        IsGreen = dot.Color == SyncDotColor.Green;
        IsYellow = dot.Color == SyncDotColor.Yellow;
        IsRed = dot.Color == SyncDotColor.Red;
        IsGrey = dot.Color == SyncDotColor.Grey;
        Accessible = SyncBoardWords.ColorWord(dot.Color) + ": " + Words;
    }

    public MaterialIconKind Icon { get; }

    public string Words { get; }

    public string Accessible { get; }

    public bool IsGreen { get; }

    public bool IsYellow { get; }

    public bool IsRed { get; }

    public bool IsGrey { get; }
}
