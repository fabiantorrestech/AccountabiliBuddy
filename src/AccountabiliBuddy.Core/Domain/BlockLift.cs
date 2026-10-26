namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A window in which some blocks may be off (ADR 37): <see cref="Allow"/> (the default) lets the user turn them off,
/// <see cref="Auto"/> turns them off at <see cref="Start"/>; both come back on at <see cref="End"/> (an end not after
/// the start is the next day).
/// </summary>
public sealed record BlockLift(TimeOnly Start, TimeOnly End, string Mode, IReadOnlyList<BlockRef> Blocks)
{
    public const string Allow = "allow";
    public const string Auto = "auto";
}
