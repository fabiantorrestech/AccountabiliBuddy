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
public sealed record SyncFile(
    int FormatVersion,
    SyncDeviceCard Device,
    IReadOnlyList<SyncSession> Sessions,
    IReadOnlyList<SyncDrift> Drift,
    SyncDayPlan? DayPlan,
    IReadOnlyList<SyncDocument> Documents)
{
    public const int CurrentFormatVersion = 1;
}
