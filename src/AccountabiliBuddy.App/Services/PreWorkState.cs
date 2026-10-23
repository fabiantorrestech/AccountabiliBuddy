namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Pre-Work on this computer (ADR 31, document "prework"): today's answer to "Is today a work day?", a day off taken
/// after its wait, Pre-Work ended by a session, and the emergency unlocks (reasons DPAPI-sealed).
/// </summary>
/// <param name="WorkDayOn">The day the user said it's a work day.</param>
/// <param name="DayOffAskedAt">When "No, day off" was chosen (its wait counts from here); null when not asked today.</param>
/// <param name="DayOffOn">The day that is a day off on this computer, once its wait was over.</param>
/// <param name="EndedOn">The day a session ended Pre-Work (starting one does).</param>
/// <param name="UnlockedUntil">An emergency unlock lifts Pre-Work until this time (the part's end).</param>
public sealed record PreWorkState(
    DateOnly? WorkDayOn = null,
    DateTimeOffset? DayOffAskedAt = null,
    DateOnly? DayOffOn = null,
    DateOnly? EndedOn = null,
    DateTimeOffset? UnlockedUntil = null,
    IReadOnlyList<EmergencyUnlock>? Unlocks = null)
{
    public static PreWorkState Empty { get; } = new();
}

/// <summary>One emergency unlock: when, and its reason sealed with DPAPI (base64).</summary>
public sealed record EmergencyUnlock(DateTimeOffset At, string ProtectedReason);
