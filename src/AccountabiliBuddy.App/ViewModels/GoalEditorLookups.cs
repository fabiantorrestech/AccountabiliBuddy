using AccountabiliBuddy.App.Services.Goals;

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
/// <param name="HiddenTagNote">ADR 89: "Uses a label from Laptop" for a tag this device doesn't show (it still counts; the name never shows here); null for any other tag.</param>
/// <param name="GroupHasPhone">NG1: a phone is in this PC's sync group. Health Connect, Pixel Watch and Screen time (measured on a phone) are offered only then; a goal that already has one keeps it, with a note.</param>
/// <param name="InSyncGroup">This PC is in a sync group (only used for the wording of the note on a phone source a goal keeps while no phone is in the group).</param>
/// <param name="Seen">ST2: what this PC saw in its own activity history in the last 30 days (the screen-time pick list and the "Not seen on this device yet" note); read when the dialog opens, never stored.</param>
/// <param name="ReadSeen">ST2: reads <paramref name="Seen"/> when a screen-time row opens (so a dialog without one never queries the history); used when <paramref name="Seen"/> is null.</param>
/// <param name="SelfDeviceId">ST2: this PC's device id, stamped on a new screen-time source (<see cref="Core.Goals.GoalSourceRule.CreatedOn"/>); empty where it isn't known.</param>
/// <param name="SelfDeviceName">ST2: this PC's name in the "Counts from" list.</param>
/// <param name="OtherDevices">ST2: the other devices of the sync group that run AccountabiliBuddy (watches left out): key = the device id as text, text = its name, icon = PC or phone.</param>
/// <param name="GoalsSyncOn">ST2: other devices' day values reach this PC (goals sync, ADR 79, follows "Sync task lists with my other devices"); with it off the other devices are listed but greyed.</param>
/// <param name="IsProductaBuddy">ProductaBuddy mode (ADR 68): the PB badge, per-mode defaults, lowering Deep work at once.</param>
public sealed record GoalEditorLookups(
    IReadOnlyList<GoalChoice> Labels,
    IReadOnlyList<GoalChoice> CalendarCategories,
    bool BeeminderConnected,
    IReadOnlyList<GoalChoice> BeeminderGoals,
    bool IsProductaBuddy,
    string? BeeminderReadText = null,
    string? BeeminderProblemText = null,
    Func<Guid, string?>? HiddenLabelNote = null,
    Func<string, string?>? HiddenTagNote = null,
    bool GroupHasPhone = true,
    bool InSyncGroup = true,
    GoalScreenTimeSeen? Seen = null,
    Func<GoalScreenTimeSeen>? ReadSeen = null,
    Guid SelfDeviceId = default,
    string SelfDeviceName = "This PC",
    IReadOnlyList<GoalChoice>? OtherDevices = null,
    bool GoalsSyncOn = true)
{
    public static GoalEditorLookups Empty { get; } = new([], [], false, [], false);
}
