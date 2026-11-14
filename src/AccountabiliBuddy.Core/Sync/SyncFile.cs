using System.Text.Json.Serialization;
using AccountabiliBuddy.Core.Goals;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// One device's sync file (ADR 22): its card, the last 14 days of its counted focus sessions and drift, its day plan and
/// its shared settings. Each device writes only its own file; a receiver replaces its copy of that device's part. Only
/// readable fields (ADR 15): never titles, addresses, OCR text, reasons or an added session's note. Sealed and opened
/// by <see cref="SyncFileCodec"/>.
/// </summary>
/// <param name="FormatVersion"><see cref="CurrentFormatVersion"/> when written; a reader refuses a newer one.</param>
/// <param name="Drift">Distracting stretches inside the sessions.</param>
/// <param name="DayPlan">Null when the file carries no plan.</param>
/// <param name="Documents">The shared settings (profiles, sleep, day bar, goal).</param>
/// <param name="CoversFrom">
/// The time the file speaks from: it holds every counted session of the device that ends after it, and the drift in
/// them, so a receiver drops what it has from the device after it (<see cref="SyncMerge"/>). Null in a file that
/// doesn't say (then <see cref="SyncMerge.CoveredFrom"/> reads it from the sessions). Optional in the JSON: written only
/// when set, so a format 1 file without it reads as before.
/// </param>
/// <param name="ActionResults">A PC's answers to the phone's actions (ADR 42); null from a PC that predates them.</param>
/// <param name="EndEarly">A PC's open end-early offer (ADR 42); null when there's none.</param>
/// <param name="GoalMinutes">A PC's daily focus goal in minutes (ADR 29); null when not sent.</param>
/// <param name="ProfileOptions">A PC's profiles, always listed (ADR 42), for the phone's start picker; null from a phone or an older PC.</param>
/// <param name="DefaultProfileId">A PC's "Quick Start with Default" profile; null when it isn't made yet.</param>
/// <param name="Noise">A PC's noise player (ADR 42, 36); null when it has no sound files.</param>
/// <param name="Placement">A phone's placement during a PC focus session (bag, desk, pocket, out); null from a PC, an older phone or when no session runs.</param>
/// <param name="NextTasks">
/// The next session's task list with its tombstones (ADR 48), merged item by item by <see cref="SharedListMerge"/>; null
/// from a QR file or an older app, which then take no part in it. Optional in the JSON, so older apps ignore it.
/// </param>
/// <param name="Parked">
/// The come-backs, the user's parked thoughts, open and recently ticked, with their tombstones (ADR 48); null as
/// <paramref name="NextTasks"/>. <see cref="Comebacks"/> stays for older phones: the open ones only.
/// </param>
/// <param name="Later">
/// The Later list with its tombstones (ADR 50), merged item by item like <paramref name="NextTasks"/>; null from a QR file
/// or an app that predates it, which then take no part in it.
/// </param>
/// <param name="PastKeys">
/// The group's past keys (ADR 56), for opening backups sealed before a later removal; a receiver merges them as a set by group id.
/// Null when the device has none, in a QR file and from an app that predates it, which ignore it. Never kept in the copy a receiver
/// stores of this file, and never in a backup.
/// </param>
/// <param name="Categories">
/// This device's own task categories, name and colour only, so the other devices can offer them when adding a task. Null in a QR
/// file and from an app that predates it, which ignore it; a device that knows it always writes it, empty when it has none. A
/// receiver keeps it in its copy of this device's file, and keeps the stored list when a file carries none.
/// </param>
/// <param name="Steps">
/// A phone's daily step counts (goal, read time, last 14 days), only while "Track daily steps" is on there. Null from a PC, a QR file
/// and an app that predates it, which ignore it. A value that can't be read is dropped by <see cref="SyncStepsConverter"/>, never
/// failing the file. Never leaves the user's own devices.
/// </param>
/// <param name="WorkEnded">
/// A PC's work ended early today (ADR 65): the plan date, the ended windows and when they were last edited, times only. Null when
/// nothing was ended or undone today, and from an app that predates it, which ignore it (an older phone keeps nudging: the stricter side).
/// </param>
/// <param name="ArmedEnds">
/// A PC's waits running in the background (ADR 66): each armed end's id, kind and ready time, times only (never the reason). Null
/// when none runs, and from an app that predates it, which ignore it (it is then cancelled only on the PC).
/// </param>
/// <param name="Guidance">
/// The guidance mode this device chose (ADR 68), mode and time only. Null for a default nobody chose and from an app that predates
/// it, which ignore it (an older device keeps guarding: the stricter side).
/// </param>
/// <param name="TaskHistory">ADR 72: a PC's own 90-day task history (never a reason; notes travel in <see cref="SessionNotes"/>, ADR 87); null from a phone, while task lists aren't shared, in a QR file and from an older app.</param>
/// <param name="PastSettled">ADR 72: the shared record of what was done with past sessions' open tasks.</param>
/// <param name="AutoSaved">ADR 72: the shared Auto-saved list.</param>
/// <param name="Handovers">ADR 72: items on their way to the device the user chose to keep them; never in a QR file.</param>
/// <param name="HandoverAcks">ADR 72: this device's receipts for the handovers it took in; never in a QR file.</param>
/// <param name="Tasks">
/// ADR 76: the one shared task list (every bin) with per-field stamps and tombstones, merged by <see cref="TaskFieldMerge"/>. Null from
/// an app that predates it, in a QR file and while task lists aren't shared; a file that carries it is the truth for that device's
/// tasks, and its old fields (<paramref name="NextTasks"/>, <paramref name="Later"/>, <paramref name="Parked"/>,
/// <paramref name="Comebacks"/>, <paramref name="Categories"/>) are then only for older apps. Read leniently
/// (<see cref="SyncLenientListConverter{T}"/>).
/// </param>
/// <param name="Labels">ADR 76: the labels (id, name, colour with opacity, rings on with its own stamp, position), merged item by item (rings on by its stamp, <see cref="TaskLabelMerge"/>); null as <paramref name="Tasks"/>.</param>
/// <param name="Views">ADR 76: the saved views (never search words), merged item by item; null as <paramref name="Tasks"/>.</param>
/// <param name="TaskLog">ADR 76: History (finished, archived, deleted tasks with their snapshot, 30 days), merged item by item; null as <paramref name="Tasks"/>. Not <paramref name="TaskHistory"/>.</param>
/// <param name="TaskAccountSettings">
/// The account's Tasks settings (the view the Shelf opens with, the picked calendar for tasks with a due date, the devices task reminders
/// ring on; ids only), merged field
/// by field by <see cref="TaskAccountSettingsMerge"/>; null as <paramref name="Tasks"/>, and when nothing was ever chosen. Read
/// leniently (<see cref="SyncLenientObjectConverter{T}"/>).
/// </param>
/// <param name="Goals">
/// ADR 79: the goals with their per-part stamps and tombstones, merged part by part by <see cref="GoalSyncMerge"/> (Deep work's
/// record composed with this device's effective weekly target, <see cref="GoalDeepWorkSync"/>; never a waiting change). Null from an
/// app that predates it, in a QR file and while task lists aren't shared; a receiver then keeps its own goals unchanged. Private text
/// (names, notes, a source's #tag or app): only inside the sealed file, never in the copy a receiver stores, never logged. Read leniently
/// and forward compatibly (<see cref="SyncGoalListConverter{T}"/>: a newer app's item is kept as written).
/// </param>
/// <param name="GoalAreas">ADR 79: the goal areas, merged item by item (newest wins); null as <paramref name="Goals"/>.</param>
/// <param name="GoalEntries">
/// ADR 79: the goal entries edited in the last 90 days (<see cref="GoalSyncWindow"/>; owner size decision 2026-11-14, the rest in the goal
/// history files), merged item by item, entry tombstones kept for good; written as compact rows (<see cref="GoalEntryRows"/>,
/// <see cref="SyncGoalEntryListConverter"/>), the GS1a object form read too; null as <paramref name="Goals"/>.
/// </param>
/// <param name="GoalReminders">ADR 79: the goal reminders (reasons are private text), merged item by item; null as <paramref name="Goals"/>.</param>
/// <param name="GoalReviews">ADR 79: the weekly review records, one per week (<see cref="GoalReviewIds"/>); null as <paramref name="Goals"/>.</param>
/// <param name="GoalYearsMerged">
/// ADR 79 shared history (GS1b): the goal year files this device has merged, each as its year, the PC that rolled it and the roll-up time of the version merged
/// (<see cref="SyncGoalYearMerged"/>, <see cref="GoalHistoryRules.Marked"/>). A month file is removed only once every active device's marker
/// lists its year file's version (<see cref="GoalHistoryRules.MayRemoveMonth"/>). Ids, numbers and times only; null when none was merged, from an
/// app that predates it and in a QR file. Read leniently.
/// </param>
/// <param name="Receipts">
/// ADR 86: what this device holds and applied of each OTHER device's files, per sync method and per part (<see cref="SyncReceipt"/>), so that device
/// can show whether its newest file arrived and was taken. Ids, times and fixed codes only. Read leniently (a bad entry is dropped) and cleaned
/// to its bounds (<see cref="SyncReceiptParts.Clean"/>); never in a QR file, kept in the receiver's stored copy (it holds no text), never in a
/// backup. Null from an app that predates it, in a QR file and when this device is alone in its group. Receiving a file never triggers a write.
/// </param>
/// <param name="SessionNotes">
/// ADR 87: the user's session notes (their text, the session start and focus profile id they were written in, the writing device's id, edit and
/// deletion times), merged item by item by <see cref="SessionNoteRules"/> (<see cref="SharedListMerge"/>: newest edit wins, tombstones 30 days, the 90-day
/// window). Private text: only inside sealed files between the user's own devices, under "Sync task lists" (ADR 71); never in a QR file, never in the
/// copy a receiver keeps of this file, never in a backup, never on the watch, never logged. Reasons never travel. Null from an app that predates it
/// (which ignores it), in a QR file and while task lists aren't shared; a file without it leaves the receiver's notes alone. Read leniently
/// (<see cref="SyncLenientListConverter{T}"/>) and cleaned (<see cref="SessionNoteRules.Clean"/>). Appended after the receipts.
/// </param>
/// <param name="TagScopes">
/// ADR 89: the tags that are device-only, each as its name-derived id, the name, the devices that show it (device ids), the edit time and a 30-day
/// tombstone (<see cref="SyncTagScope"/>), merged item by item (<see cref="TagScopeMerge"/>). A tag with no live scope is shared. The label's own
/// "only on" list rides in <see cref="Labels"/>. Ids and the tag names the tasks already carry; sealed files between the user's own devices under
/// "Sync task lists" only, never in a QR file, never in the copy a receiver keeps of the file, never logged. Null from an app that predates it
/// (which shows every tag), in a QR file and while task lists aren't shared; a file without it leaves the receiver's scopes alone. Read leniently
/// and cleaned (<see cref="Tasks.TaskScopes.Clean"/>). Appended last. Receipts count it with the "tasks" part.
/// </param>
public sealed record SyncFile(
    int FormatVersion,
    SyncDeviceCard Device,
    IReadOnlyList<SyncSession> Sessions,
    IReadOnlyList<SyncDrift> Drift,
    SyncDayPlan? DayPlan,
    IReadOnlyList<SyncDocument> Documents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? CoversFrom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncComeback>? Comebacks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncPickup>? Pickups = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncLunch? Lunch = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncAction>? Actions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Guid>? Acks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncRunning? Running = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] System.Text.Json.JsonElement? Events = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncInputRun>? InputRuns = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncSmartBedtime? SmartBedtime = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncWakeEstimate? WakeEstimate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncReminderAction>? ReminderActions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncBlocker>? Blockers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncActionResult>? ActionResults = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncEndEarlyOffer? EndEarly = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? GoalMinutes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncProfileOption>? ProfileOptions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DefaultProfileId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncBeeminderGoal>? Beeminder = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncNoise? Noise = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncPlacement? Placement = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncNextTask>? NextTasks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncParked>? Parked = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncLaterItem>? Later = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncPastKey>? PastKeys = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncCategoriesConverter))] IReadOnlyList<SyncCategory>? Categories = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncSteps? Steps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncWorkEnded? WorkEnded = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncArmedEnd>? ArmedEnds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncGuidance? Guidance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncTaskHistoryEntry>? TaskHistory = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncPastSettled>? PastSettled = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncAutoSavedItem>? AutoSaved = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncHandover>? Handovers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncHandoverAck>? HandoverAcks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncTaskItem>))] IReadOnlyList<SyncTaskItem>? Tasks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncTaskLabel>))] IReadOnlyList<SyncTaskLabel>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncTaskView>))] IReadOnlyList<SyncTaskView>? Views = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncTaskLogItem>))] IReadOnlyList<SyncTaskLogItem>? TaskLog = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientObjectConverter<SyncTaskAccountSettings>))] SyncTaskAccountSettings? TaskAccountSettings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncGoalListConverter<Goal>))] IReadOnlyList<Goal>? Goals = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncGoalListConverter<GoalArea>))] IReadOnlyList<GoalArea>? GoalAreas = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncGoalEntryListConverter))] IReadOnlyList<GoalEntry>? GoalEntries = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncGoalListConverter<GoalReminder>))] IReadOnlyList<GoalReminder>? GoalReminders = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncGoalListConverter<GoalReviewRecord>))] IReadOnlyList<GoalReviewRecord>? GoalReviews = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncGoalYearMerged>))] IReadOnlyList<SyncGoalYearMerged>? GoalYearsMerged = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncReceipt>))] IReadOnlyList<SyncReceipt>? Receipts = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncSessionNote>))] IReadOnlyList<SyncSessionNote>? SessionNotes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] [property: JsonConverter(typeof(SyncLenientListConverter<SyncTagScope>))] IReadOnlyList<SyncTagScope>? TagScopes = null)
{
    public const int CurrentFormatVersion = 1;
}
