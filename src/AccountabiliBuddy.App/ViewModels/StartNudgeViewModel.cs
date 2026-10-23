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
    }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        _ = _nudge.SaveAsync(_nudge.Settings with { On = value });
    }
}
