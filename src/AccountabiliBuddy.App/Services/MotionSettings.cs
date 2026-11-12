namespace AccountabiliBuddy.App.Services;

/// <summary>How fast the app's animations run (document "motion"); Off shows every change at once.</summary>
public enum MotionSpeed
{
    Normal,
    Fast,
    Slow,
    Off,
}

/// <summary>Which way the touchpad's two-finger swipe moves through the pages (NAV2, owner 2026-11-14).</summary>
public enum SwipeStyle
{
    /// <summary>Swipe right = Back, swipe left = Forward (like Chrome on Windows).</summary>
    Default,

    /// <summary>The opposite: swipe left = Back, swipe right = Forward (like a Mac).</summary>
    Natural,
}

/// <summary>
/// The app's animation setting (Settings → On screen &amp; sound → Animations) and, beside it in the same device-local document,
/// the swipe direction (Settings → On screen &amp; sound → Touchpad swipe). Never synced, in the PC backup's device group.
/// </summary>
public sealed record MotionSettings(MotionSpeed Speed = MotionSpeed.Normal, SwipeStyle Swipe = SwipeStyle.Default)
{
    public static MotionSettings Default { get; } = new();
}
