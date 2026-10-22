namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A wake time changed for one night only (e.g. up at 7:15 tomorrow instead of the plan's 6:45). It applies to the
/// morning of <paramref name="Morning"/> and nowhere else; the day plan stays as it is.
/// </summary>
/// <param name="OverridesAlarm">True when the user confirmed this wake time over the phone's alarm for that morning (ADR 23).</param>
public sealed record WakeOnce(DateOnly Morning, TimeOnly Wake, bool OverridesAlarm = false);
