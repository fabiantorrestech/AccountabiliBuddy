namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A device's day plan as it travels (ADR 19, 22), with what the clash rule needs: its version, when it was last edited,
/// and the version that edit started from.
/// </summary>
/// <param name="PlanJson">The plan, from <see cref="Storage.ProfileDocumentSerializer.SerializeDayPlan"/>.</param>
/// <param name="Version">From <see cref="Engine.DayPlanVersion.Of"/>.</param>
/// <param name="BasedOn">The version the device held before its last edit; null when it isn't known.</param>
public sealed record SyncDayPlan(string PlanJson, string Version, DateTimeOffset EditedAt, string? BasedOn);
