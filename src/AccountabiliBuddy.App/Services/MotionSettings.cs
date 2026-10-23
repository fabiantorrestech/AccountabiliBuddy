namespace AccountabiliBuddy.App.Services;

/// <summary>How fast the app's animations run (document "motion"); Off shows every change at once.</summary>
public enum MotionSpeed
{
    Normal,
    Fast,
    Slow,
    Off,
}

/// <summary>The app's animation setting (Settings → On screen &amp; sound → Animations).</summary>
public sealed record MotionSettings(MotionSpeed Speed = MotionSpeed.Normal)
{
    public static MotionSettings Default { get; } = new();
}
