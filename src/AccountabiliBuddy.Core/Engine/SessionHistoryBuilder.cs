using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Pairs session start and end events into <see cref="SessionRecord"/>s for the History list. A start followed by
/// another start (the app was closed mid-session and did not resume) becomes a record without an end.
/// Interrupted/Resumed pairs inside a session add up to <see cref="SessionRecord.AppClosedFor"/>.
/// </summary>
public static class SessionHistoryBuilder
{
    /// <param name="events">Events oldest first.</param>
    /// <returns>Sessions newest first.</returns>
    public static IReadOnlyList<SessionRecord> Build(IEnumerable<SessionEvent> events)
    {
        var records = new List<SessionRecord>();
        SessionEvent? open = null;
        DateTimeOffset? interruptedAt = null;
        TimeSpan closedFor = TimeSpan.Zero;

        foreach (SessionEvent sessionEvent in events)
        {
            switch (sessionEvent.Kind)
            {
                case SessionEventKind.Started:
                    if (open is not null)
                    {
                        records.Add(new SessionRecord(open.At, open.Mode, open.ProfileId, null, null, null, closedFor));
                    }

                    open = sessionEvent;
                    interruptedAt = null;
                    closedFor = TimeSpan.Zero;
                    continue;

                case SessionEventKind.Interrupted:
                    interruptedAt = sessionEvent.At;
                    continue;

                case SessionEventKind.Resumed:
                    if (interruptedAt is DateTimeOffset since && sessionEvent.At > since)
                    {
                        closedFor += sessionEvent.At - since;
                    }

                    interruptedAt = null;
                    continue;
            }

            if (open is null)
            {
                continue; // The start fell outside the queried period.
            }

            records.Add(new SessionRecord(
                open.At, open.Mode, open.ProfileId, sessionEvent.At, sessionEvent.Kind, sessionEvent.Reason, closedFor));
            open = null;
            closedFor = TimeSpan.Zero;
        }

        if (open is not null)
        {
            records.Add(new SessionRecord(open.At, open.Mode, open.ProfileId, null, null, null, closedFor));
        }

        records.Reverse();
        return records;
    }
}
