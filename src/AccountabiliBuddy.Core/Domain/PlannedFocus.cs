namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Focus time the schedule plans: one occurrence of a weekly or one-off block, with time off already taken out
/// (so a block cut by time off becomes two).
/// </summary>
public sealed record PlannedFocus(Guid ProfileId, LocalWindow Window);
