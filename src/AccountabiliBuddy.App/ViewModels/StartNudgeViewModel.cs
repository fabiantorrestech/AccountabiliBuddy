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
        IsAssistantOn = nudge.Settings.AssistantOn;
        RepeatIndex = Math.Max(0, Array.IndexOf(RepeatChoices, nudge.Settings.RepeatMinutes));
    }

    /// <summary>The mode switched (ADR 69): shows the active mode's switches.</summary>
    public void ReloadForMode(bool companion)
    {
        IsCompanion = companion;
        IsOn = _nudge.Settings.On;
        IsAssistantOn = _nudge.Settings.AssistantOn;
        RepeatIndex = Math.Max(0, Array.IndexOf(RepeatChoices, _nudge.Settings.RepeatMinutes));
    }

    /// <summary>"Ask again every": 5, 10 or 15 minutes, or one card only (0).</summary>
    public static int[] RepeatChoices { get; } = [5, 10, 15, 0];

    public static IReadOnlyList<string> RepeatNames { get; } = ["5 minutes", "10 minutes", "15 minutes", "Only once"];

    [ObservableProperty]
    public partial int RepeatIndex { get; set; }

    partial void OnRepeatIndexChanged(int value)
    {
        _ = _nudge.SaveAsync(_nudge.Settings with { RepeatMinutes = RepeatChoices[Math.Clamp(value, 0, RepeatChoices.Length - 1)] });
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetAssistant))]
    public partial bool IsOn { get; set; }

    /// <summary>ProductaBuddy is in effect (ADR 68, 69): only the start-of-day assistant's switch shows, and it works on its own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetAssistant))]
    public partial bool IsCompanion { get; set; }

    /// <summary>The assistant needs the nudge on in AccountabiliBuddy; in ProductaBuddy it stands alone.</summary>
    public bool CanSetAssistant => IsOn || IsCompanion;

    partial void OnIsOnChanged(bool value)
    {
        _ = _nudge.SaveAsync(_nudge.Settings with { On = value });
    }

    /// <summary>The first arrival of a workday opens the start-of-day assistant (hours, first tasks, Start); it needs the nudge on.</summary>
    [ObservableProperty]
    public partial bool IsAssistantOn { get; set; }

    partial void OnIsAssistantOnChanged(bool value)
    {
        _ = _nudge.SaveAsync(_nudge.Settings with { AssistantOn = value });
    }
}
