using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Import;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Session tab: Cold Turkey's blocks, on or off, and which ones the running session's profile expects.
/// Hidden when Cold Turkey isn't installed or can't be read.
/// </summary>
public sealed partial class ColdTurkeyStatusViewModel : ObservableObject
{
    private static readonly IBrush OnBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.FromRgb(229, 72, 77));
    private static readonly IBrush OffBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));

    private readonly ColdTurkeyService _coldTurkey;
    private readonly SessionController _sessions;

    public ColdTurkeyStatusViewModel(ColdTurkeyService coldTurkey, SessionController sessions)
    {
        _coldTurkey = coldTurkey;
        _sessions = sessions;
        Summary = string.Empty;

        _coldTurkey.Changed += (_, _) => Refresh();
        _sessions.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<ColdTurkeyBlockRow> Blocks { get; } = new();

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>E.g. "Cold Turkey: 2 of 5 blocks on".</summary>
    [ObservableProperty]
    public partial string Summary { get; set; }

    private void Refresh()
    {
        ColdTurkeyBlocks? blocks = _coldTurkey.Blocks;
        IsVisible = blocks is not null;
        Blocks.Clear();
        if (blocks is null)
        {
            return;
        }

        IReadOnlyList<string> expected = _sessions.State.IsActive && _sessions.State.ProfileId is Guid profileId
            ? _coldTurkey.ExpectedFor(profileId)
            : Array.Empty<string>();
        foreach (ColdTurkeyBlockState block in blocks.Blocks.OrderBy(block => block.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool on = _coldTurkey.OnNow.Contains(block.Name);
            bool isExpected = expected.Contains(block.Name);
            string state = on ? "on"
                : block.IsSwitchedOn && !blocks.IsPaused ? "switched on, not scheduled now"
                : "off";
            Blocks.Add(new ColdTurkeyBlockRow(
                block.Name,
                isExpected ? $"{state} · this session expects it on" : state,
                on ? OnBrush : isExpected ? MissingBrush : OffBrush));
        }

        // Expected by the profile but gone from Cold Turkey (renamed or deleted).
        foreach (string name in expected.Where(name => blocks.Blocks.All(block => block.Name != name)))
        {
            Blocks.Add(new ColdTurkeyBlockRow(name, "not in Cold Turkey any more · this session expects it", MissingBrush));
        }

        Summary = blocks.IsPaused
            ? "Cold Turkey is paused: none of its blocks are on"
            : $"Cold Turkey: {_coldTurkey.OnNow.Count} of {blocks.Blocks.Count} blocks on";
    }
}
