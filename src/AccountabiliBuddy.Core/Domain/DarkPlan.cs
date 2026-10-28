namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// Whether the app should look dark at a moment, and when that next flips (set times or sunset, ADR 49).
/// <paramref name="UsedSetTimes"/> is true when sunset could not be worked out (polar day or night) and the set times were used.
/// </summary>
public sealed record DarkPlan(bool IsDark, DateTimeOffset NextSwitch, bool UsedSetTimes);
