namespace AccountabiliBuddy.Core.Sync;

/// <summary>One counted stretch of the day's lunch (ADR 88): when it started and when it ended. A lunch can be taken in several pieces.</summary>
public sealed record LunchPiece(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>How long the piece lasted (never negative).</summary>
    public TimeSpan Length => End > Start ? End - Start : TimeSpan.Zero;
}
