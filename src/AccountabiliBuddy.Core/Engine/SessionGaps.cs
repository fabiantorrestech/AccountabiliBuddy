using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Distracted stretches marked by hand on past sessions (<see cref="SessionGap"/>): folded into a record's away
/// stretches (kind <see cref="AwayKind.Distracted"/>) so every focus total leaves them out, checked before saving, and
/// used to cut a session into the pieces the user was really there for (what other devices get).
/// </summary>
public static class SessionGaps
{
    /// <summary>The records with their marked gaps added to <see cref="SessionRecord.Away"/>, cut to the session.</summary>
    public static IReadOnlyList<SessionRecord> Apply(IReadOnlyList<SessionRecord> records, Func<DateTimeOffset, IReadOnlyList<SessionGap>> gapsOf)
    {
        return records.Select(record =>
        {
            IReadOnlyList<SessionGap> gaps = gapsOf(record.StartedAt);
            if (gaps.Count == 0 || record.EndedAt is not DateTimeOffset end)
            {
                return record;
            }

            IEnumerable<AwayStretch> marked = gaps
                .Select(gap => new AwayStretch(gap.From < record.StartedAt ? record.StartedAt : gap.From, gap.To > end ? end : gap.To, AwayKind.Distracted))
                .Where(stretch => stretch.To > stretch.From);
            return record with { Away = (record.Away ?? []).Concat(marked).OrderBy(stretch => stretch.From).ToList() };
        }).ToList();
    }

    /// <summary>
    /// Why <paramref name="gaps"/> can't be saved for the session from <paramref name="start"/> to
    /// <paramref name="end"/>, or null when they can: each ends after it starts, lies inside the session, and none
    /// overlap (touching is fine).
    /// </summary>
    public static string? Validate(DateTimeOffset start, DateTimeOffset? end, IReadOnlyList<SessionGap> gaps)
    {
        if (end is not DateTimeOffset sessionEnd)
        {
            return "This session hasn't ended yet. Mark distracted time once it has.";
        }

        foreach (SessionGap gap in gaps)
        {
            if (gap.To <= gap.From)
            {
                return "Each distracted stretch has to end after it starts.";
            }

            if (gap.From < start || gap.To > sessionEnd)
            {
                return "Each distracted stretch has to lie inside the session's start and end.";
            }
        }

        List<SessionGap> ordered = gaps.OrderBy(gap => gap.From).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].From < ordered[i - 1].To)
            {
                return "Two distracted stretches overlap. Make them one, or move one.";
            }
        }

        return null;
    }

    /// <summary>
    /// The parts of the session from <paramref name="start"/> to <paramref name="end"/> without
    /// <paramref name="away"/> (time stepped away and marked distracted), in order.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Pieces(DateTimeOffset start, DateTimeOffset end, IEnumerable<AwayStretch> away)
    {
        var pieces = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        DateTimeOffset at = start;
        foreach (AwayStretch stretch in away.Where(stretch => stretch.To > start && stretch.From < end).OrderBy(stretch => stretch.From))
        {
            if (stretch.From > at)
            {
                pieces.Add((at, stretch.From));
            }

            if (stretch.To > at)
            {
                at = stretch.To;
            }
        }

        if (end > at)
        {
            pieces.Add((at, end));
        }

        return pieces;
    }
}
