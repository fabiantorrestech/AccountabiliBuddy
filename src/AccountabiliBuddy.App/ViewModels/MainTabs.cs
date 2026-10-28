namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What each <see cref="MainTab"/> shows around its page.</summary>
public static class MainTabs
{
    /// <summary>
    /// The day bar's strip above the page: no page shows it any more. Today carries its own timeline under "More today", and
    /// Stats draws its day in the right column (design 5p).
    /// </summary>
    public static bool ShowsTimelineStrip(MainTab tab)
    {
        return false;
    }
}
