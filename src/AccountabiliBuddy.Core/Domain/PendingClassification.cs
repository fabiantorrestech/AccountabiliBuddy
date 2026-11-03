namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A classification change that waits for the looser-change delay before it takes over (ADR 58): the old choice stays in force
/// until then.
/// </summary>
/// <param name="Change">The choice to store; for a removal only its target (calendar, key and scope) counts.</param>
/// <param name="Removes">True to take the choice back (so the rules or the plan decide again) instead of storing <paramref name="Change"/>.</param>
/// <param name="TakesOverAt">Trusted-clock time the change takes over, if no session the user started is running then.</param>
public sealed record PendingClassification(EventClassification Change, bool Removes, DateTimeOffset TakesOverAt);
