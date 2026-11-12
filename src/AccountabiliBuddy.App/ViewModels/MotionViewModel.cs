using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Settings → On screen &amp; sound → Animations: Normal, Fast, Slow or Off; applies at once.</summary>
public sealed partial class MotionViewModel : ObservableObject
{
    private readonly Motion _motion;

    public MotionViewModel(Motion motion)
    {
        _motion = motion;
        SpeedIndex = (int)motion.Settings.Speed;
        SwipeIndex = (int)motion.Settings.Swipe;
    }

    /// <summary>In the order of <see cref="SwipeStyle"/>.</summary>
    public static IReadOnlyList<string> SwipeNames { get; } = ["Default", "Natural"];

    /// <summary>The row's hint (the setting applies at once).</summary>
    public const string SwipeHint = "Which way a two-finger swipe on the touchpad goes. Default: swipe right to go back, left to go forward, like Chrome on Windows. Natural: swipe left to go back, like a Mac.";

    [ObservableProperty]
    public partial int SwipeIndex { get; set; }

    partial void OnSwipeIndexChanged(int value)
    {
        var swipe = (SwipeStyle)Math.Clamp(value, 0, SwipeNames.Count - 1);
        if (swipe != _motion.Settings.Swipe)
        {
            _ = _motion.SaveAsync(_motion.Settings with { Swipe = swipe });
        }
    }

    /// <summary>In the order of <see cref="MotionSpeed"/>.</summary>
    public static IReadOnlyList<string> SpeedNames { get; } = ["Normal", "Fast", "Slow", "Off"];

    [ObservableProperty]
    public partial int SpeedIndex { get; set; }

    partial void OnSpeedIndexChanged(int value)
    {
        var speed = (MotionSpeed)Math.Clamp(value, 0, SpeedNames.Count - 1);
        if (speed != _motion.Settings.Speed)
        {
            _ = _motion.SaveAsync(_motion.Settings with { Speed = speed });
        }
    }
}
