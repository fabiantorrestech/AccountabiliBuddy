using System.Text.Json;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The <see cref="SyncAction.Text"/> of a <see cref="SyncAction.KindDayPlanEdit"/> action (ADR 42, the transport for
/// ADR 19), as JSON: <c>{ "basedOn": "…", "editedAt": "…", "plan": { … } }</c>.
/// </summary>
/// <param name="BasedOn">The PC plan version (<see cref="SyncDayPlan.Version"/>) the phone edited from.</param>
/// <param name="EditedAt">When it was saved on the phone.</param>
/// <param name="Plan">The whole edited plan, in the profile document's <c>dayPlan</c> shape (as <see cref="SyncDayPlan.PlanJson"/>).</param>
public sealed record SyncDayPlanEditRequest(string BasedOn, DateTimeOffset EditedAt, JsonElement Plan);
