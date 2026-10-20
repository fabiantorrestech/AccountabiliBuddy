namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// When a night's sleep should start and end. A bedtime before noon (e.g. 00:30) is after midnight, on the day
/// after the night's evening.
/// </summary>
public sealed record SleepTimes(TimeOnly Bedtime, TimeOnly Wake);
