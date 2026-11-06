namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// The work an armed <see cref="ArmedEndKind.EndWorkEarly"/> ends: the block the dialog showed (<paramref name="Current"/>) and,
/// when <paramref name="AlsoEndLater"/>, the later blocks it listed. Local times on the plan date.
/// </summary>
public sealed record ArmedWorkEnd(DateOnly PlanDate, LocalWindow Current, IReadOnlyList<LocalWindow> Later, bool AlsoEndLater);
