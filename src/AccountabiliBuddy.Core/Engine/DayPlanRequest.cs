using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The answers of the "Plan a day" walkthrough (ADR 37): the dates, an adjusted workday (copied from Workdays) or an
/// off-day (copied from Off-Days), the night before each date (null = as the template has it), the block rules
/// (null = as the template has them), an optional note, and <paramref name="NoSleep"/>: leave the night out (not
/// recommended; wins over <paramref name="Sleep"/>).
/// </summary>
public sealed record DayPlanRequest(IReadOnlyList<DateOnly> Dates, bool OffDay, SleepTimes? Sleep, BlockRules? Blocks, string? Note = null, bool NoSleep = false);
