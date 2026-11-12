namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// The This week card's weekly review row (round 2 R5.4, change note §4a): whether it shows, whether it is snoozed (ProductaBuddy's
/// snooze keeps a quiet row), whether Snooze is offered, and its note ("Sundays · about 4 min, then the tidy-up").
/// </summary>
/// <param name="IsShown">The row shows (due and not done, no session, not snoozed in AccountabiliBuddy mode).</param>
/// <param name="IsSnoozed">It is snoozed now.</param>
/// <param name="CanSnooze">Snooze is offered (not while already snoozed).</param>
/// <param name="Note">The muted line under "Weekly review".</param>
/// <param name="SnoozeTip">Snooze's tooltip ("Snooze the weekly review for 1 day").</param>
public sealed record GoalReviewRow(bool IsShown, bool IsSnoozed, bool CanSnooze, string Note, string SnoozeTip = "Snooze the weekly review");
