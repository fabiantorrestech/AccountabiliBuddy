namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What each <see cref="MainTab"/> shows around its page.</summary>
public static class MainTabs
{
    /// <summary>
    /// The day bar's strip above the page: Stats only. Today carries its own timeline under "More today", so the page
    /// is not pushed down by a second copy of it.
    /// </summary>
    public static bool ShowsTimelineStrip(MainTab tab)
    {
        return tab is MainTab.Stats;
    }
}
