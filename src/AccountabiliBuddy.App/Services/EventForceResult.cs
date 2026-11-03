namespace AccountabiliBuddy.App.Services;

/// <summary>What <see cref="EffectiveDayPlanProvider.ForceOf"/> found for one classified event.</summary>
/// <param name="Level">How much of the event the overlay let in.</param>
/// <param name="CountsTowardBudget">
/// Part of the event lies over a Budget day's productive stretch and the overlay would credit it there as it happens (it had
/// its notice and no session blocks it). A meeting that hasn't started has earned nothing yet; this only says it will count.
/// </param>
/// <param name="OverBudgetStretch">Part of the event lies over a Budget day's productive stretch, credited or not.</param>
public sealed record EventForceResult(ForceLevel Level, bool CountsTowardBudget, bool OverBudgetStretch);
