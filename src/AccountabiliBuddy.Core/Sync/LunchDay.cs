namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The day's lunch as one pure rule (ADR 88; the phone's Kotlin twin LunchDay is the same rule). A day has an allowance (the lunch length
/// when the first piece started, +5 and -5 moving it) that can be taken in pieces: "Count it" ends the running piece and counts it, the rest stays
/// available for a later piece; "Discard it" takes the running piece back as if it never started, when the mode's setting allows it. Every device
/// reads the same record, so "40 left" is the same on all of them whatever each one's own length setting says. No piece ever takes the day's
/// total past <see cref="MaxLength"/>. Times come from the caller (the trusted clock on the PC, the trusted time on the phone).
/// </summary>
public static class LunchDay
{
    /// <summary>The day's lunch is never longer than this in total, whatever +5, another device or a stale file says.</summary>
    public const int MaxMinutes = 120;

    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(MaxMinutes);

    /// <summary>How much +5 / -5 min changes a running lunch by.</summary>
    public static readonly TimeSpan AdjustStep = TimeSpan.FromMinutes(5);

    /// <summary>A shortened piece ends no sooner than this after now, and a new piece needs at least this much left.</summary>
    public static readonly TimeSpan ShortestLeft = TimeSpan.FromMinutes(1);

    /// <summary>The counted pieces added up (the current piece is not in them).</summary>
    public static TimeSpan CountedTotal(SyncLunch lunch) =>
        TimeSpan.FromTicks((lunch.Counted ?? []).Sum(piece => piece.Length.Ticks));

    /// <summary>The day's lunch in minutes: the record's own, else <paramref name="fallbackMinutes"/> (an older app's record carries none); 0 to 120.</summary>
    public static int AllowanceMinutes(SyncLunch lunch, int fallbackMinutes) => Math.Clamp(lunch.Allowance ?? fallbackMinutes, 0, MaxMinutes);

    /// <summary>The lunch used so far: the counted pieces plus the current piece up to <paramref name="now"/> (or its end). A discarded piece used nothing.</summary>
    public static TimeSpan Used(SyncLunch lunch, DateTimeOffset now)
    {
        TimeSpan current = lunch.Discarded ? TimeSpan.Zero : Max(TimeSpan.Zero, Min(now, lunch.End) - lunch.Start);
        return CountedTotal(lunch) + current;
    }

    /// <summary>What is left of the day's allowance, never negative.</summary>
    public static TimeSpan Remaining(SyncLunch lunch, DateTimeOffset now, int fallbackMinutes) =>
        Max(TimeSpan.Zero, TimeSpan.FromMinutes(AllowanceMinutes(lunch, fallbackMinutes)) - Used(lunch, now));

    /// <summary>True while a piece runs: not discarded, and not over.</summary>
    public static bool IsRunning(SyncLunch? lunch, DateTimeOffset now) => lunch is not null && !lunch.Discarded && now < lunch.End;

    /// <summary>Another piece can start: nothing runs and at least <see cref="ShortestLeft"/> of the allowance is left (a day with no lunch yet can always start).</summary>
    public static bool CanStart(SyncLunch? lunch, DateTimeOffset now, int fallbackMinutes) =>
        lunch is null || (!IsRunning(lunch, now) && Remaining(lunch, now, fallbackMinutes) >= ShortestLeft);

    /// <summary>"Discard it" is offered: a piece runs and the mode's setting allows it (<paramref name="allowedByMode"/>); any time in the piece.</summary>
    public static bool CanDiscard(SyncLunch? lunch, DateTimeOffset now, bool allowedByMode) => allowedByMode && IsRunning(lunch, now);

    /// <summary>A fixed lunch starts by itself only on a day with no lunch record at all: a discarded lunch is still a record, so it never starts again by itself.</summary>
    public static bool AutoStartPending(SyncLunch? lunch) => lunch is null;

    /// <summary>The first piece of the day: runs <paramref name="length"/> from <paramref name="now"/> (held to 1 minute to 120), and that length is the day's allowance.</summary>
    public static SyncLunch Begin(DateTimeOffset now, TimeSpan length)
    {
        TimeSpan held = Max(ShortestLeft, Min(length, MaxLength));
        return new SyncLunch(now, now + held, now, CeilMinutes(held), [], false);
    }

    /// <summary>
    /// A new piece after the last one: it runs for what is left. Null when a piece runs or too little is left. <paramref name="lunch"/> must be today's
    /// lunch (the caller passes only the lunch of today; there is no day check here).
    /// </summary>
    public static SyncLunch? StartAgain(SyncLunch lunch, DateTimeOffset now, int fallbackMinutes)
    {
        if (IsRunning(lunch, now))
        {
            return null;
        }

        TimeSpan remaining = Remaining(lunch, now, fallbackMinutes);
        if (remaining < ShortestLeft)
        {
            return null;
        }

        List<LunchPiece> counted = [.. lunch.Counted ?? []];
        if (!lunch.Discarded && lunch.End > lunch.Start)
        {
            counted.Add(new LunchPiece(lunch.Start, lunch.End));
        }

        return Held(new SyncLunch(now, now + remaining, now, AllowanceMinutes(lunch, fallbackMinutes), counted, false));
    }

    /// <summary>
    /// "Count it" (and "I'm back"): the running piece ends now and counts; the rest of the allowance stays for later. Null when nothing runs.
    /// The end is held to the day's total (<see cref="Held"/>), so the counted minutes never add up to more than <see cref="MaxLength"/>.
    /// </summary>
    public static SyncLunch? Count(SyncLunch lunch, DateTimeOffset now) =>
        IsRunning(lunch, now) ? Held(lunch with { End = Max(now, lunch.Start), EditedAt = now }) : null;

    /// <summary>
    /// "Discard it": the running piece never happened. Null when nothing runs or the mode doesn't allow it. The first piece becomes a zero-length record
    /// marked <see cref="SyncLunch.Discarded"/> (the day stays taken); a later piece goes back to the last counted one, which leaves the list.
    /// <paramref name="lunch"/> must be today's lunch (the caller passes only the lunch of today; there is no day check here).
    /// </summary>
    public static SyncLunch? Discard(SyncLunch lunch, DateTimeOffset now, bool allowedByMode)
    {
        if (!CanDiscard(lunch, now, allowedByMode))
        {
            return null;
        }

        IReadOnlyList<LunchPiece> counted = lunch.Counted ?? [];
        if (counted.Count > 0)
        {
            LunchPiece last = counted[^1];
            return lunch with { Start = last.Start, End = last.End, Counted = [.. counted.Take(counted.Count - 1)], Discarded = false, EditedAt = now };
        }

        return lunch with { End = lunch.Start, Discarded = true, EditedAt = now };
    }

    /// <summary>
    /// +5 (positive) or -5 on the running piece, the allowance moving with it: never ends sooner than a minute from now, a -5 never makes it longer,
    /// and the day's total never goes past <see cref="MaxLength"/>. Null when nothing runs or nothing changes.
    /// </summary>
    public static SyncLunch? Adjust(SyncLunch lunch, DateTimeOffset now, TimeSpan change, int fallbackMinutes)
    {
        if (!IsRunning(lunch, now))
        {
            return null;
        }

        DateTimeOffset end = lunch.End;
        DateTimeOffset newEnd = end + change;
        DateTimeOffset latest = lunch.Start + Max(TimeSpan.Zero, MaxLength - CountedTotal(lunch));
        if (newEnd > latest)
        {
            newEnd = latest > end ? latest : end;
        }

        DateTimeOffset soonest = now + ShortestLeft;
        if (newEnd < soonest)
        {
            newEnd = soonest < end ? soonest : end;
        }

        if (newEnd == end)
        {
            return null;
        }

        int moved = RoundMinutes(newEnd - end);
        return lunch with { End = newEnd, EditedAt = now, Allowance = Math.Clamp(AllowanceMinutes(lunch, fallbackMinutes) + moved, 0, MaxMinutes) };
    }

    /// <summary>
    /// The record held to its limits: the allowance 0 to 120, the current piece ends so that the day's total is at most <see cref="MaxLength"/>, and a
    /// discarded piece is zero length (End == Start), so a "discarded" lunch can never read as running.
    /// </summary>
    public static SyncLunch Held(SyncLunch lunch)
    {
        DateTimeOffset latestEnd = lunch.Start + Max(TimeSpan.Zero, MaxLength - CountedTotal(lunch));
        SyncLunch held = lunch.End > latestEnd ? lunch with { End = latestEnd } : lunch;
        if (held.Discarded && held.End != held.Start)
        {
            held = held with { End = held.Start };
        }

        return held.Allowance is int allowance && (allowance < 0 || allowance > MaxMinutes) ? held with { Allowance = Math.Clamp(allowance, 0, MaxMinutes) } : held;
    }

    /// <summary>
    /// The lunch to take from another device's file, or null when nothing changes here (the caller checks that it is today's, and ignores a record
    /// identical to the one last received from the same sender). The stamp is held to <paramref name="now"/> (<see cref="LunchListening.ClampFuture"/>)
    /// and the day's total to <see cref="MaxLength"/>. A record whose <see cref="SyncLunch.Counted"/> is missing (not empty) is an older app's edit:
    /// when its start is ours it keeps our pieces and allowance (the +5 or End on piece 2 must not wipe the counted minutes; it is discarded only if
    /// it is still zero length, so an older app's +5 on a discarded lunch makes a running lunch, never a "discarded" one that runs), and when its
    /// start is one of our counted pieces it is a stale view of the day and changes nothing. The other way round, when our own pieces or allowance
    /// are unknown (we took an older app's edit first) and an older or equal record with the same start carries them, we take its pieces and allowance
    /// whatever its stamp, so the order the files arrive in never decides what is counted. Otherwise the newer edit wins, a tie in the stamp going to
    /// the same side on every device (<see cref="Outranks"/>), and the same pieces change nothing.
    /// </summary>
    public static SyncLunch? Receive(SyncLunch? ours, SyncLunch received, DateTimeOffset now)
    {
        SyncLunch lunch = LunchListening.ClampFuture(received, now);
        SyncLunch? fill = null;
        if (lunch.Counted is null && ours is not null)
        {
            if ((ours.Counted ?? []).Any(piece => piece.Start == lunch.Start))
            {
                return null;
            }

            if (lunch.Start == ours.Start)
            {
                lunch = lunch with { Counted = ours.Counted, Allowance = ours.Allowance ?? lunch.Allowance, Discarded = ours.Discarded && lunch.End <= lunch.Start };
                lunch = lunch.Allowance is null ? lunch : lunch with { Allowance = Covering(lunch) };
            }
        }
        else if (ours is not null && lunch.Start == ours.Start && lunch.LastEdit <= ours.LastEdit && (ours.Counted is null || ours.Allowance is null))
        {
            SyncLunch filled = ours with { Counted = ours.Counted ?? lunch.Counted, Allowance = ours.Allowance ?? lunch.Allowance };
            filled = Held(filled.Allowance is null ? filled : filled with { Allowance = Covering(filled) });
            if (!SamePieces(ours, filled))
            {
                // Our filled record is the one the received record is compared with; it is kept when the received record does not beat it.
                fill = filled;
                ours = filled;
            }
        }

        lunch = Held(lunch);
        return ours is not null && (!Newer(lunch, ours) || SamePieces(ours, lunch)) ? fill : lunch;
    }

    /// <summary>The same lunch apart from the edit stamp: current piece, allowance, discarded mark and counted pieces (missing and empty alike).</summary>
    public static bool SamePieces(SyncLunch a, SyncLunch b) =>
        a.Start == b.Start && a.End == b.End && a.Allowance == b.Allowance && a.Discarded == b.Discarded
        && (a.Counted ?? []).SequenceEqual(b.Counted ?? []);

    /// <summary>
    /// Whether the device whose lunch is <paramref name="theirs"/> has <paramref name="ours"/> (LunchDelivery): the same current piece, and the same
    /// counted pieces and allowance when theirs sends them (an older app sends neither, so it is judged on the current piece alone: it can only ever
    /// hold the current piece, so "has it" there means "has the piece it can show"; a newer app that took an older app's edit first no longer stays that
    /// way, because it fills in its pieces from the next file with the same start, see <see cref="Receive"/>).
    /// </summary>
    public static bool HasPieces(SyncLunch theirs, SyncLunch ours) =>
        theirs.Start == ours.Start && theirs.End == ours.End && theirs.Discarded == ours.Discarded
        && (theirs.Counted is null || theirs.Counted.SequenceEqual(ours.Counted ?? []))
        && (theirs.Allowance is null || theirs.Allowance == ours.Allowance);

    /// <summary>The allowance raised to cover the counted pieces and the current piece (an older app may have lengthened it), at most <see cref="MaxMinutes"/>.</summary>
    private static int Covering(SyncLunch lunch)
    {
        TimeSpan used = CountedTotal(lunch) + (lunch.End > lunch.Start ? lunch.End - lunch.Start : TimeSpan.Zero);
        return Math.Min(MaxMinutes, Math.Max(lunch.Allowance ?? 0, CeilMinutes(used)));
    }

    /// <summary>Whether <paramref name="received"/> replaces <paramref name="ours"/>: a newer stamp, or the same stamp and a record that <see cref="Outranks"/> ours.</summary>
    private static bool Newer(SyncLunch received, SyncLunch ours) =>
        received.LastEdit > ours.LastEdit || (received.LastEdit == ours.LastEdit && Outranks(received, ours));

    /// <summary>
    /// The one fixed order for two different records with the same stamp, so devices that disagree at the same stamp all end on the same record (the
    /// phone's Kotlin twin is the same order): known counted pieces before unknown, more counted pieces, then start, end, allowance (unknown lowest),
    /// discarded, and the counted pieces one by one.
    /// </summary>
    private static bool Outranks(SyncLunch a, SyncLunch b)
    {
        int order = (a.Counted is null ? 0 : 1).CompareTo(b.Counted is null ? 0 : 1);
        order = order != 0 ? order : (a.Counted?.Count ?? 0).CompareTo(b.Counted?.Count ?? 0);
        order = order != 0 ? order : a.Start.CompareTo(b.Start);
        order = order != 0 ? order : a.End.CompareTo(b.End);
        order = order != 0 ? order : (a.Allowance ?? -1).CompareTo(b.Allowance ?? -1);
        order = order != 0 ? order : a.Discarded.CompareTo(b.Discarded);
        for (int i = 0; order == 0 && i < (a.Counted?.Count ?? 0); i++)
        {
            order = a.Counted![i].Start.CompareTo(b.Counted![i].Start);
            order = order != 0 ? order : a.Counted[i].End.CompareTo(b.Counted[i].End);
        }

        return order > 0;
    }

    /// <summary>Whole minutes of <paramref name="length"/> rounded up, exact to the tick (the phone's twin counts the same way); 0 for nothing or less.</summary>
    public static int CeilMinutes(TimeSpan length)
    {
        long ticks = length.Ticks;
        return ticks <= 0 ? 0 : (int)Math.Min(int.MaxValue, (ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute);
    }

    /// <summary>Whole minutes of <paramref name="length"/> rounded to the nearest, a half minute away from zero (+30 s is 1, -30 s is -1), exact to the tick; the phone's twin does the same.</summary>
    public static int RoundMinutes(TimeSpan length)
    {
        long ticks = length.Ticks;
        long minutes = ticks / TimeSpan.TicksPerMinute;
        if (Math.Abs(ticks % TimeSpan.TicksPerMinute) * 2 >= TimeSpan.TicksPerMinute)
        {
            minutes += ticks < 0 ? -1 : 1;
        }

        return (int)minutes;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
