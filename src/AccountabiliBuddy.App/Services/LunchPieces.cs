using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The pieces a day's lunch was taken in (ADR 88), for the day bar and the timeline: the counted pieces, then the current one. A discarded piece (or
/// one of no length) was never taken, so it isn't drawn.
/// </summary>
public static class LunchPieces
{
    /// <summary>Every piece of <paramref name="lunch"/> that was taken, earliest first.</summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Of(SyncLunch lunch)
    {
        var pieces = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (LunchPiece counted in lunch.Counted ?? [])
        {
            pieces.Add((counted.Start, counted.End));
        }

        if (!lunch.Discarded && lunch.End > lunch.Start)
        {
            pieces.Add((lunch.Start, lunch.End));
        }

        return pieces;
    }
}
