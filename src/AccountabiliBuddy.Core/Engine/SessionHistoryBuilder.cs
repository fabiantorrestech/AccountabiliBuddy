using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Pairs session start and end events into <see cref="SessionRecord"/>s for the History list. A start followed by
/// another start (the app was closed mid-session) becomes a record without an end.
/// </summary>
public static class SessionHistoryBuilder
{
    /// <param name="events">Events oldest first.</param>
    /// <returns>Sessions newest first.</returns>
    public static IReadOnlyList<SessionRecord> Build(IEnumerable<SessionEvent> events)
    {
        var records = new List<SessionRecord>();
        SessionEvent? open = null;

        foreach (SessionEvent sessionEvent in events)
        {
            if (sessionEvent.Kind == SessionEventKind.Started)
            {
                if (open is not null)
                {
                    records.Add(new SessionRecord(open.At, open.Mode, open.ProfileId, null, null, null));
                }

                open = sessionEvent;
                continue;
            }

            if (open is null)
            {
                continue; // The start fell outside the queried period.
            }

            records.Add(new SessionRecord(
                open.At, open.Mode, open.ProfileId, sessionEvent.At, sessionEvent.Kind, sessionEvent.Reason));
            open = null;
        }

        if (open is not null)
        {
            records.Add(new SessionRecord(open.At, open.Mode, open.ProfileId, null, null, null));
        }

        records.Reverse();
        return records;
    }
}
