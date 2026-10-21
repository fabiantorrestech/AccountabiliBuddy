using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// What a focus session added by hand (ADR 20) must respect: at least a minute, at most 24 hours, and no overlap with
/// any other session, recorded or added, omitted or not (an omitted session still happened). Touching end to start
/// is fine. The session running now has no end yet, so nothing can be added from its start on; an older recorded
/// session that never got an end (the app stopped) is left out, as the day bar leaves it out.
/// </summary>
public static class AddedSessionRules
{
    public static readonly TimeSpan Shortest = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Longest = TimeSpan.FromHours(24);

    /// <param name="sessions">The sessions around that time (recorded and added).</param>
    /// <param name="editingStart">The start of the added session being edited, which doesn't count against itself.</param>
    /// <param name="sessions">Newest first, as <see cref="SessionHistoryBuilder.Build"/> gives them.</param>
    /// <param name="sessionRunning">True while a focus session runs: the newest recorded one, if it has no end yet.</param>
    /// <returns>Null when it can be saved; otherwise why not.</returns>
    public static string? Validate(DateTimeOffset start, DateTimeOffset end, IReadOnlyList<SessionRecord> sessions, DateTimeOffset? editingStart, bool sessionRunning)
    {
        if (end <= start)
        {
            return "The end must be after the start.";
        }

        if (end - start < Shortest)
        {
            return "A session lasts at least a minute.";
        }

        if (end - start > Longest)
        {
            return "A session can't be longer than 24 hours.";
        }

        SessionRecord? newestRecorded = sessions.FirstOrDefault(session => session.Mode != SessionMode.Added);
        foreach (SessionRecord session in sessions)
        {
            if (session.Mode == SessionMode.Added && session.StartedAt == editingStart)
            {
                continue;
            }

            if (session.EndedAt is not DateTimeOffset sessionEnd)
            {
                if (sessionRunning && session == newestRecorded && end > session.StartedAt)
                {
                    return $"It overlaps the session running now (since {session.StartedAt.LocalDateTime:t}).";
                }

                continue;
            }

            if (start < sessionEnd && session.StartedAt < end)
            {
                string what = session.Mode == SessionMode.Added ? "another added session" : "a session the app recorded";
                return $"It overlaps {what} ({session.StartedAt.LocalDateTime:t} – {sessionEnd.LocalDateTime:t}).";
            }
        }

        return null;
    }
}
