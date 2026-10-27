using System.Text.Json;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>A device change to the day plan (ADR 19): the whole plan, and the PC version it was edited from.</summary>
/// <param name="BaseVersion">The <c>dayPlanVersion</c> of the PC's plan the device started from.</param>
/// <param name="DayPlan">The edited plan, in the profile document's <c>dayPlan</c> shape.</param>
/// <param name="EditedAt">When it was saved on the device.</param>
public sealed record DayPlanEdit(string BaseVersion, JsonElement DayPlan, DateTimeOffset EditedAt);
