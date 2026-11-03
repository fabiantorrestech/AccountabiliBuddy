namespace AccountabiliBuddy.Core.Domain;

/// <summary>What a stretch with the PC off or the app closed does to the session (ADR 60).</summary>
public enum SessionGapOutcome
{
    /// <summary>Under <see cref="Engine.SessionGapPolicy.ShortGap"/>: the session carries on, with no notice.</summary>
    Quiet,

    /// <summary>The session carries on with its timer paused for the gap.</summary>
    Paused,

    /// <summary>The session carries on with its original end time.</summary>
    KeptEnd,

    /// <summary>With the original end kept, the end passed during the gap: it closed when the PC went off.</summary>
    EndPassed,

    /// <summary>A long gap or a new day: it closed when the PC went off.</summary>
    ClosedLongGap,
}
