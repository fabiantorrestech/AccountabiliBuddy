using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A session put back at launch after the app stopped without ending it.</summary>
/// <param name="State">The session as it was saved, before catching up on phases that ran out meanwhile.</param>
/// <param name="LastAliveAt">When the app was last known to be running.</param>
public sealed record SessionResume(SessionState State, DateTimeOffset LastAliveAt, DateTimeOffset ResumedAt)
{
    /// <summary>How long the app was closed (at least; it may have stopped up to one save interval later).</summary>
    public TimeSpan ClosedFor => ResumedAt > LastAliveAt ? ResumedAt - LastAliveAt : TimeSpan.Zero;
}
