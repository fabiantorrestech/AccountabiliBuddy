using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One block in "Plan day(s)" (ADR 37): whether it's on now, whether it comes back on at its device's time (ticked)
/// or at its own (unticked, <see cref="OwnTime"/>), and whether to lift it for part of the day.
/// </summary>
public sealed partial class PlanDayBlockRow : ObservableObject
{
    public PlanDayBlockRow(BlockRef block, string deviceName, bool on)
    {
        Block = block;
        DeviceName = deviceName;
        IsEnabled = on;
    }

    public BlockRef Block { get; }

    public string DeviceName { get; }

    public string Name => Block.Name;

    public bool IsEnabled { get; }

    public string StateText => IsEnabled ? "→ Enabled" : "→ Disabled";

    /// <summary>For the lift list: "This PC · Social".</summary>
    public string Label => $"{DeviceName} · {Block.Name}";

    /// <summary>Comes back on at its device's time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnTime))]
    public partial bool FollowsDevice { get; set; } = true;

    public bool HasOwnTime => !FollowsDevice;

    [ObservableProperty]
    public partial TimeSpan? OwnTime { get; set; } = TimeSpan.Zero;

    [ObservableProperty]
    public partial bool Lift { get; set; }
}
