namespace AccountabiliBuddy.App.Services;

/// <summary>How the session's progress is drawn (stored as a number).</summary>
public enum OverlayProgress
{
    Ring,

    /// <summary>A 4 px line: done in Accent, what is left in Upcoming, the break in Sleep, what is next in Plan, and a thin ink tick for now.</summary>
    Timeline,
}
