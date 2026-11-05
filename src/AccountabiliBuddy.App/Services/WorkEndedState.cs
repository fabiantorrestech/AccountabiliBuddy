namespace AccountabiliBuddy.App.Services;

/// <summary>Work ended early on this PC or taken from another (document "workEnded", ADR 65): one entry per plan date, kept 90 days.</summary>
public sealed record WorkEndedState(IReadOnlyList<WorkEndedDay>? Days = null);
