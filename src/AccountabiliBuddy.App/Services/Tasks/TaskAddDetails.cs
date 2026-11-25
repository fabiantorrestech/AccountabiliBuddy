using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// What the task editor's new-task popup adds on top of the add box's title, bin, label, tags and note (Tasks redesign item 5): its steps
/// in order, the highlight, the time limit and the due date (Next and Later only, as the editor shows it). The step titles are the user's
/// text: never logged.
/// </summary>
/// <param name="Steps">The steps typed in the popup, in order; blank ones are left out.</param>
/// <param name="Highlight">Highlighted (star and tint) from the start.</param>
/// <param name="EstimateMinutes">The time limit; null for none.</param>
/// <param name="DueOn">"Done by" date; null for none.</param>
/// <param name="Destination">Where a first due date puts it ("Add new reminders to", Settings › Reminders).</param>
public sealed record TaskAddDetails(
    IReadOnlyList<string> Steps, bool Highlight, int? EstimateMinutes, DateOnly? DueOn, TaskCalendarDestination Destination);
