namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A device's day plan as it travels (ADR 19, 22), with what the clash rule (<see cref="DayPlanClash"/>) needs: its
/// version, when it was last edited, and the newest version the device shares with the others. A device stamps its own
/// plan with the methods below, so several edits between two files still read as one device's change.
/// </summary>
/// <param name="PlanJson">The plan, from <see cref="Storage.ProfileDocumentSerializer.SerializeDayPlan"/>.</param>
/// <param name="Version">From <see cref="Engine.DayPlanVersion.Of"/>.</param>
/// <param name="EditedAt">
/// When the plan was last edited, on whichever device; it travels with the plan. A plan nobody has edited (a new
/// device's default) can say the earliest time, so any edited plan is newer.
/// </param>
/// <param name="BasedOn">
/// The newest version the device shares with the others: the version it last took from another device or saw in another
/// device's file (then it equals <see cref="Version"/>), or, after edits here, the shared version they started from.
/// Further edits don't move it. Null when it isn't known (a plan edited before the device joined): it counts as edited.
/// </param>
public sealed record SyncDayPlan(string PlanJson, string Version, DateTimeOffset EditedAt, string? BasedOn)
{
    /// <returns>True when the device edited the plan since it last took or shared one (or that isn't known).</returns>
    public bool HasLocalEdits()
    {
        return !string.Equals(BasedOn, Version, StringComparison.Ordinal);
    }

    /// <returns>The plan after an edit on this device: <see cref="BasedOn"/> stays, however many edits come before the next file.</returns>
    public SyncDayPlan Edited(string planJson, string version, DateTimeOffset editedAt)
    {
        return this with { PlanJson = planJson, Version = version, EditedAt = editedAt };
    }

    /// <returns>
    /// The plan once shared: taken from another device (<see cref="DayPlanSyncDecision.TakeIncoming"/>) or found in its
    /// file (<see cref="DayPlanSyncDecision.Same"/>). It is based on its own version.
    /// </returns>
    public SyncDayPlan Shared()
    {
        return this with { BasedOn = Version };
    }

    /// <returns>
    /// This device's plan kept over <paramref name="other"/> (after "Keep the one I was using", or when it was the
    /// latest): it reads as an edit of the other plan, so the other device takes it without asking.
    /// </returns>
    public SyncDayPlan KeptOver(SyncDayPlan other)
    {
        return this with { BasedOn = other.Version };
    }
}
