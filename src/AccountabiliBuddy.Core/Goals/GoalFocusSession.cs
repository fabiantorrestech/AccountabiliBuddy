using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One focus session as the goals see it (<see cref="GoalFocusAttribution"/>): its counted focus as local windows (time away
/// already out) and the tasks of its checklist, which say which label and #tag goals its minutes count toward.
/// </summary>
/// <param name="Windows">Its counted focus, local wall-clock time, earliest first.</param>
/// <param name="Tasks">Its checklist's tasks (each with its label as the category, ADR 76, and its #tags as labels); empty for none.</param>
/// <param name="Added">Added by hand in History (ADR 20): counts toward goals, never toward a guard.</param>
public sealed record GoalFocusSession(IReadOnlyList<LocalWindow> Windows, IReadOnlyList<SessionTask> Tasks, bool Added);
