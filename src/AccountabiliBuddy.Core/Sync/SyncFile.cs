using System.Text.Json.Serialization;

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
/// <param name="Documents">The shared settings (profiles, sleep, day bar).</param>
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SyncBeeminderGoal>? Beeminder = null)
{
    public const int CurrentFormatVersion = 1;
}
