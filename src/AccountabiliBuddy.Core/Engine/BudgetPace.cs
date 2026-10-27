namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// How a Budget day is going (ADR 47): the productive time still to find and the time still available for it today.
/// </summary>
/// <param name="BudgetLeft">The budget minus today's counted focus so far; zero once met.</param>
/// <param name="AvailableLeft">What's left of today's guarded windows from now on (waking time minus the non-productive windows).</param>
/// <param name="Behind">True when the available time is shorter than the budget left: the day can't be made up any more.</param>
public sealed record BudgetPace(TimeSpan BudgetLeft, TimeSpan AvailableLeft, bool Behind);
