namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A bedtime reminder dismissed or snoozed on a device (ADR 38): which night ("2026-10-25"), which reminder (n),
/// "dismiss" or "snooze", when, and for a snooze when it rings again. The others stop it ringing. Kept a day.
/// </summary>
public sealed record SyncReminderAction(string Night, int Number, string Action, DateTimeOffset At, DateTimeOffset? Until = null);
