namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// When a night's sleep should start and end. A bedtime before noon (e.g. 00:30) is after midnight, on the day
/// after the night's evening.
/// </summary>
/// <param name="WakeByAlarm">The wake time is the morning's alarm, else bedtime plus the sleep goal (ADR 33); <see cref="Wake"/> is then only a fallback for drawing.</param>
public sealed record SleepTimes(TimeOnly Bedtime, TimeOnly Wake, bool WakeByAlarm = false);
