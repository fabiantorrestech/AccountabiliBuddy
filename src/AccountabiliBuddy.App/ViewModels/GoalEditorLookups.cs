namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// What the goal dialogs pick from (round 2 R5.4, R5.7), read when a dialog opens: this PC's task labels, the day plan categories (for
/// Google Calendar time, ADR 58), Beeminder's cached goals and last read (ADR 43, 84: never fetched here), and the mode the app is in.
/// </summary>
/// <param name="Labels">Task labels: key = the label's id (<see cref="Core.Domain.TaskLabel.IdFor"/> its name).</param>
/// <param name="CalendarCategories">Day plan categories that calendar events are classified into: key = the category's id.</param>
/// <param name="BeeminderConnected">Beeminder is connected in Integrations (the Beeminder tile shows only then, brief §6.2).</param>
/// <param name="BeeminderGoals">The cached goals list: key = the goal's slug (or title), note = what's needed · safe days.</param>
/// <param name="BeeminderReadText">"Read 2:05 PM": when the datapoints of the linked goals were last read (ADR 84); null before the first read.</param>
/// <param name="BeeminderProblemText">"Couldn't reach Beeminder · keeps the last values" when the last read failed; null otherwise.</param>
/// <param name="HiddenLabelNote">ADR 89: "Uses a label from Laptop" for a label id this device doesn't show (it still counts; the name never shows here); null for any other id.</param>
/// <param name="IsProductaBuddy">ProductaBuddy mode (ADR 68): the PB badge, per-mode defaults, lowering Deep work at once.</param>
public sealed record GoalEditorLookups(
    IReadOnlyList<GoalChoice> Labels,
    IReadOnlyList<GoalChoice> CalendarCategories,
    bool BeeminderConnected,
    IReadOnlyList<GoalChoice> BeeminderGoals,
    bool IsProductaBuddy,
    string? BeeminderReadText = null,
    string? BeeminderProblemText = null,
    Func<Guid, string?>? HiddenLabelNote = null)
{
    public static GoalEditorLookups Empty { get; } = new([], [], false, [], false);
}
