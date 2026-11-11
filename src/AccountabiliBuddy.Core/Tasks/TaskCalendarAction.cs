namespace AccountabiliBuddy.Core.Tasks;

/// <summary>One write to the calendar (ADR 78), from <see cref="TaskCalendarDiff.Plan"/>.</summary>
/// <param name="Recorded">The event written before (Delete, Update); null for Create.</param>
/// <param name="Wanted">The event it should be (Create, Update); null for Delete.</param>
public sealed record TaskCalendarAction(TaskCalendarActionKind Kind, Guid TaskId, TaskCalendarRecord? Recorded, TaskCalendarWanted? Wanted);
