using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Everything the timeline's rows show for one day, before it is placed on the bar.</summary>
/// <param name="Range">The stretch of time the bar covers.</param>
/// <param name="Parts">The day plan's parts on the dates around the range (clipped by the builder).</param>
/// <param name="Blockers">Cold Turkey's blocks; null when they aren't shown (switched off in Settings, or not readable).</param>
/// <param name="Focused">Time focused today, for the Focus row's label.</param>
/// <param name="Unfocused">Work time (the plan's Watch parts, up to now) with no focus session.</param>
/// <param name="TaskMarks">Guided sessions' ticked tasks (ADR 24), as marks at their tick times.</param>
public sealed record TimelineDay(
    LocalWindow Range,
    IReadOnlyList<PlannedPart> Parts,
    IReadOnlyList<TimelineSession> Sessions,
    IReadOnlyList<TimelineItem> Drift,
    IReadOnlyList<TimelineItem> Events,
    IReadOnlyList<BlockerWindow>? Blockers,
    TimeSpan Focused,
    IReadOnlyList<LocalWindow>? Unfocused = null,
    IReadOnlyList<TimelineItem>? TaskMarks = null);
