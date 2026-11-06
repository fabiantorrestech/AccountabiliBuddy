using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The guidance mode's rules (ADR 68), the same on every device. Switching to Companion is looser: it waits the looser-change
/// delay, is refused during a session and applies only with none running. Switching to Guarding is stricter: at once. Between
/// devices the newest edit wins; a received Companion waits the receiver's delay from its edit time.
/// </summary>
public static class GuidanceRules
{
    // A stamp may run at most this far ahead of this device's clock (the rule other synced stamps follow).
    private static readonly TimeSpan MaxStampAhead = TimeSpan.FromMinutes(2);

    /// <summary>Asks for Companion: it waits <paramref name="delay"/>. Null when refused (a session runs).</summary>
    public static GuidanceState? RequestCompanion(GuidanceState state, DateTimeOffset now, TimeSpan delay, bool sessionActive)
    {
        if (state.Mode == GuidanceMode.Companion || state.Pending is not null)
        {
            return state;
        }

        return sessionActive ? null : state with { Pending = new GuidancePending(now, now + delay) };
    }

    /// <summary>
    /// During a focus session, "after this session ends": it waits <paramref name="delay"/> and no session running, so it applies at
    /// the later of the two. Allowed during a session because it changes nothing until the session is over.
    /// </summary>
    public static GuidanceState RequestCompanionAfterSession(GuidanceState state, DateTimeOffset now, TimeSpan delay)
    {
        if (state.Mode == GuidanceMode.Companion || state.Pending is not null)
        {
            return state;
        }

        return state with { Pending = new GuidancePending(now, now + delay) };
    }

    /// <summary>
    /// During a focus session, "now": the caller has paid what ending the session early costs (its wait and a reason). It applies
    /// after <paramref name="delay"/> even if the session still runs, and replaces a switch waiting for the session to end.
    /// </summary>
    public static GuidanceState RequestCompanionNow(GuidanceState state, DateTimeOffset now, TimeSpan delay)
    {
        if (state.Mode == GuidanceMode.Companion)
        {
            return state;
        }

        return state with { Pending = new GuidancePending(now, now + delay, DuringSession: true) };
    }

    /// <summary>
    /// "Keep" on the setup page: the starting default in effect becomes a choice, with no wait since nothing changes here. It is then
    /// sent, and other devices take it by the usual rules (a looser one after their own wait).
    /// </summary>
    public static GuidanceState ConfirmDefault(GuidanceState state, DateTimeOffset now)
    {
        return state.IsChosen || state.Pending is not null ? state : state with { EditedAt = now };
    }

    /// <summary>Guarding at once, cancelling any waiting switch (stricter, so never refused).</summary>
    public static GuidanceState ChooseGuarding(DateTimeOffset now)
    {
        return new GuidanceState(GuidanceMode.Guarding, now, null);
    }

    /// <summary>
    /// Applies a waiting switch once its time has come and no session runs; one paid for during the session
    /// (<see cref="GuidancePending.DuringSession"/>) applies with the session still running.
    /// </summary>
    public static GuidanceState ApplyIfDue(GuidanceState state, DateTimeOffset now, bool sessionActive)
    {
        if (state.Pending is not GuidancePending pending || (sessionActive && !pending.DuringSession) || now < pending.ApplyAt)
        {
            return state;
        }

        return new GuidanceState(GuidanceMode.Companion, pending.EditedAt, null);
    }

    /// <summary>
    /// Takes another device's choice when it is newer than this device's and than a switch waiting here. Guarding applies at once;
    /// Companion waits <paramref name="delay"/> from its edit time (<see cref="ApplyIfDue"/>).
    /// </summary>
    public static GuidanceState Receive(GuidanceState state, SyncGuidance received, DateTimeOffset now, TimeSpan delay)
    {
        GuidanceMode? mode = received.Mode switch
        {
            SyncGuidance.ModeGuarding => GuidanceMode.Guarding,
            SyncGuidance.ModeCompanion => GuidanceMode.Companion,
            _ => null,
        };
        if (mode is null || received.EditedAt > now + MaxStampAhead)
        {
            return state;
        }

        if (state.EditedAt is DateTimeOffset ours && ours >= received.EditedAt)
        {
            return state;
        }

        if (state.Pending is GuidancePending waiting && waiting.EditedAt >= received.EditedAt)
        {
            return state;
        }

        if (mode == GuidanceMode.Guarding)
        {
            return new GuidanceState(GuidanceMode.Guarding, received.EditedAt, null);
        }

        if (state.Mode == GuidanceMode.Companion)
        {
            return state with { EditedAt = received.EditedAt, Pending = null };
        }

        return state with { Pending = new GuidancePending(received.EditedAt, received.EditedAt + delay) };
    }

    /// <summary>What goes in the sync file: the chosen mode, or null for a default nobody chose.</summary>
    public static SyncGuidance? ForSync(GuidanceState state)
    {
        return state.EditedAt is DateTimeOffset editedAt
            ? new SyncGuidance(state.Mode == GuidanceMode.Companion ? SyncGuidance.ModeCompanion : SyncGuidance.ModeGuarding, editedAt)
            : null;
    }
}
