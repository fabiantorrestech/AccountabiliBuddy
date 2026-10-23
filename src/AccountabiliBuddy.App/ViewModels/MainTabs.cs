namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What each <see cref="MainTab"/> shows around its page.</summary>
public static class MainTabs
{
    /// <summary>The day bar sits above the page only where the day is the point: Today and History.</summary>
    public static bool ShowsTimeline(MainTab tab)
    {
        return tab is MainTab.Today or MainTab.History;
    }

    /// <summary>
    /// Focus mode: only Today and History in the sidebar while a session runs, until "Show everything" is chosen for
    /// that session.
    /// </summary>
    public static bool IsFocusMode(bool sessionActive, bool showEverything)
    {
        return sessionActive && !showEverything;
    }

    /// <summary>Folded under "More" in the sidebar: the things set once.</summary>
    public static bool IsUnderMore(MainTab tab)
    {
        return tab is MainTab.Setup or MainTab.Integrations or MainTab.Settings or MainTab.About;
    }
}
