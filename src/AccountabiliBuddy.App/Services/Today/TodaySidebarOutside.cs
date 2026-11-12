namespace AccountabiliBuddy.App.Services.Today;

/// <summary>The Today sidebar outside a session (Settings › Today › Side column › Outside a session).</summary>
public enum TodaySidebarOutside
{
    /// <summary>Open or hidden the way it was last left outside a session, until the app restarts (the default; it starts open).</summary>
    Remember,

    /// <summary>Open whenever no session runs (Ctrl+\ still hides it until the next session ends).</summary>
    Open,

    /// <summary>Hidden to the rail whenever no session runs (Ctrl+\ still opens it until the next session ends).</summary>
    Rail,
}
