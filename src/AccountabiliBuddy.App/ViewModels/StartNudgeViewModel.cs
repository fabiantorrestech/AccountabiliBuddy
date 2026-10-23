using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Settings → Your day → "Steer me to start": a nudge, not a block, so it saves at once.</summary>
public sealed partial class StartNudgeViewModel : ObservableObject
{
    private readonly StartNudgeService _nudge;

    public StartNudgeViewModel(StartNudgeService nudge)
    {
        _nudge = nudge;
        IsOn = nudge.Settings.On;
        RepeatIndex = Math.Max(0, Array.IndexOf(RepeatChoices, nudge.Settings.RepeatMinutes));
    }

    /// <summary>"Nudge again every": 5, 10 or 15 minutes, or only once (0).</summary>
    public static int[] RepeatChoices { get; } = [5, 10, 15, 0];

    public static IReadOnlyList<string> RepeatNames { get; } = ["5 minutes", "10 minutes", "15 minutes", "Only once"];

    [ObservableProperty]
    public partial int RepeatIndex { get; set; }

    partial void OnRepeatIndexChanged(int value)
    {
        _ = _nudge.SaveAsync(_nudge.Settings with { RepeatMinutes = RepeatChoices[Math.Clamp(value, 0, RepeatChoices.Length - 1)] });
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        _ = _nudge.SaveAsync(_nudge.Settings with { On = value });
    }
}
