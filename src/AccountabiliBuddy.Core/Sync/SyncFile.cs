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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SyncLunch? Lunch = null)
{
    public const int CurrentFormatVersion = 1;
}
