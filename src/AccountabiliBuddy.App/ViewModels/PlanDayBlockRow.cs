using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One block in "Plan a day" (ADR 37): which device it's on, whether it's on now, and whether to lift it.</summary>
public sealed partial class PlanDayBlockRow : ObservableObject
{
    public PlanDayBlockRow(BlockRef block, string deviceName, bool on)
    {
        Block = block;
        Label = $"{deviceName} · {block.Name}{(on ? "" : " (off now)")}";
    }

    public BlockRef Block { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial bool Lift { get; set; }
}
