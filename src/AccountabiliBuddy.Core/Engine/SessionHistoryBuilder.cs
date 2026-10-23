using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Pairs session start and end events into <see cref="SessionRecord"/>s for the History list. A start followed by
/// another start (the app was closed mid-session and did not resume) becomes a record without an end.
/// Interrupted/Resumed pairs inside a session add up to <see cref="SessionRecord.AppClosedFor"/>; AwayStarted/AwayEnded
/// pairs become <see cref="SessionRecord.Away"/> (an away with no return closes at the session's end). Sessions added by hand
/// (ADR 20) are paired on their own, so one placed inside a recorded session never cuts it.
/// </summary>
public static class SessionHistoryBuilder
{
    /// <param name="events">Events oldest first.</param>
    /// <returns>Sessions newest first.</returns>
    public static IReadOnlyList<SessionRecord> Build(IEnumerable<SessionEvent> events)
    {
        List<SessionEvent> all = events.ToList();
        List<SessionRecord> records = Pair(all.Where(sessionEvent => sessionEvent.Mode != SessionMode.Added));
        records.AddRange(Pair(all.Where(sessionEvent => sessionEvent.Mode == SessionMode.Added)));
        return records.OrderByDescending(record => record.StartedAt).ToList();
    }

    /// <summary>Pairs one stream of events, oldest first, into sessions oldest first.</summary>
    private static List<SessionRecord> Pair(IEnumerable<SessionEvent> events)
    {
        var records = new List<SessionRecord>();
        SessionEvent? open = null;
        DateTimeOffset? interruptedAt = null;
        TimeSpan closedFor = TimeSpan.Zero;
        DateTimeOffset? awaySince = null;
        AwayKind awayKind = AwayKind.Break;
        List<AwayStretch>? away = null;

        foreach (SessionEvent sessionEvent in events)
        {
            switch (sessionEvent.Kind)
            {
                case SessionEventKind.Started:
                    if (open is not null)
                    {
                        records.Add(new SessionRecord(open.At, open.Mode, open.ProfileId, null, null, null, closedFor,
                            CloseAway(away, awaySince, awayKind, DateTimeOffset.MaxValue)));
                    }

                    open = sessionEvent;
                    interruptedAt = null;
                    closedFor = TimeSpan.Zero;
                    awaySince = null;
                    away = null;
                    continue;

                case SessionEventKind.AwayStarted:
                    if (open is not null && awaySince is null)
                    {
                        awaySince = sessionEvent.At;
                        // The reason holds the kind's name; anything else (or none) reads as a break.
                        awayKind = Enum.TryParse(sessionEvent.Reason, out AwayKind kind) ? kind : AwayKind.Break;
                    }

                    continue;

                case SessionEventKind.AwayEnded:
                    if (awaySince is DateTimeOffset left && sessionEvent.At >= left)
                    {
                        (away ??= []).Add(new AwayStretch(left, sessionEvent.At, awayKind));
                    }

                    awaySince = null;
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
                open.At, open.Mode, open.ProfileId, sessionEvent.At, sessionEvent.Kind, sessionEvent.Reason, closedFor,
                CloseAway(away, awaySince, awayKind, sessionEvent.At)));
            open = null;
            closedFor = TimeSpan.Zero;
            awaySince = null;
            away = null;
        }

        if (open is not null)
        {
            records.Add(new SessionRecord(open.At, open.Mode, open.ProfileId, null, null, null, closedFor,
                CloseAway(away, awaySince, awayKind, DateTimeOffset.MaxValue)));
        }

        return records;
    }

    /// <summary>The session's away stretches, with one still going closed at <paramref name="end"/>.</summary>
    private static IReadOnlyList<AwayStretch>? CloseAway(List<AwayStretch>? away, DateTimeOffset? awaySince, AwayKind kind, DateTimeOffset end)
    {
        if (awaySince is DateTimeOffset since && end >= since)
        {
            (away ??= []).Add(new AwayStretch(since, end, kind));
        }

        return away;
    }
}
