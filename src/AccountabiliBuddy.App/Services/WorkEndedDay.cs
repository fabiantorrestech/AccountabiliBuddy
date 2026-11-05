namespace AccountabiliBuddy.App.Services;

/// <summary>
/// One plan date's work ended early (ADR 65): the ended windows (plain times), when they were last ended or undone, and the
/// reasons given here, each sealed with DPAPI (never synced; a day taken from another PC has none).
/// </summary>
public sealed record WorkEndedDay(
    DateOnly Date, IReadOnlyList<WorkEndedWindow> Windows, DateTimeOffset EditedAt, IReadOnlyList<WorkEndedReason>? Reasons = null);
