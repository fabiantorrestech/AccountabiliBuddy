namespace AccountabiliBuddy.App.Services.Today;

/// <summary>The Today sidebar while a session runs (Settings › Today › Side column › During a session).</summary>
public enum TodaySidebarInSession
{
    /// <summary>A thin rail at the right edge; Ctrl+\ slides the sidebar over the tasks until it is hidden again (the default).</summary>
    Hidden,

    /// <summary>Slid over the tasks but faded, until the pointer or the keyboard is on it.</summary>
    Quiet,

    /// <summary>Slid over the tasks, as outside a session.</summary>
    Shown,
}
