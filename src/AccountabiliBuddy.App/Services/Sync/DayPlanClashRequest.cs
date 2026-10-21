using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The day plan was changed on this PC and on another device before they synced (ADR 22): the user picks "Keep the one
/// I was using" or "Use the latest" (<see cref="SyncCoordinator.ResolveClashAsync"/>); closed or ignored, the latest
/// edit wins.
/// </summary>
/// <param name="Local">This PC's plan, with when it was last edited.</param>
/// <param name="Incoming">The other device's plan, with when it was last edited.</param>
/// <param name="FromDevice">The other device's name.</param>
/// <param name="Changes">What using the other device's plan would change here (<see cref="Core.Engine.DayPlanChanges"/>).</param>
public sealed record DayPlanClashRequest(SyncDayPlan Local, SyncDayPlan Incoming, string FromDevice, IReadOnlyList<string> Changes);
