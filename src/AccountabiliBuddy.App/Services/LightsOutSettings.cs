namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Lights out on this computer (design K, opt-in; app-owned document "lightsOut"): during work hours the work-hours
/// guard is keeping, lock the computer at each work part's start and after 3 idle minutes.
/// </summary>
/// <param name="On">Off by default.</param>
/// <param name="Sleep">Sleep instead of lock (stronger; downloads pause).</param>
public sealed record LightsOutSettings(bool On, bool Sleep)
{
    public static LightsOutSettings Default { get; } = new(false, false);
}
