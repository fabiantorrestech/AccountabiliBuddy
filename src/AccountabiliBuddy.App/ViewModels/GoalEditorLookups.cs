namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What the goal dialogs pick from (round 2 R5.4, R5.7), read when a dialog opens: this PC's task labels, the day plan categories (for
/// Google Calendar time, ADR 58), Beeminder's cached goals (ADR 43: never fetched here), and the mode the app is in.
/// </summary>
/// <param name="Labels">Task labels: key = the label's id (<see cref="Core.Domain.TaskLabel.IdFor"/> its name).</param>
/// <param name="CalendarCategories">Day plan categories that calendar events are classified into: key = the category's id.</param>
/// <param name="BeeminderConnected">Beeminder is connected in Integrations (the Beeminder tile shows only then, brief §6.2).</param>
/// <param name="BeeminderGoals">The cached goals list: key = the goal's slug (or title), note = what's needed · safe days.</param>
/// <param name="IsProductaBuddy">ProductaBuddy mode (ADR 68): the PB badge, per-mode defaults, lowering Deep work at once.</param>
public sealed record GoalEditorLookups(
    IReadOnlyList<GoalChoice> Labels,
    IReadOnlyList<GoalChoice> CalendarCategories,
    bool BeeminderConnected,
    IReadOnlyList<GoalChoice> BeeminderGoals,
    bool IsProductaBuddy)
{
    public static GoalEditorLookups Empty { get; } = new([], [], false, [], false);
}
