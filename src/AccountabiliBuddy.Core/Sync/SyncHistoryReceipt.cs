namespace AccountabiliBuddy.Core.Sync;

/// <summary>A goal history month file merged from a sender (ADR 86): the month and the written time of the version merged.</summary>
/// <param name="Month">"yyyy-MM".</param>
/// <param name="Version">The month file's written time.</param>
public sealed record SyncHistoryReceipt(string Month, DateTimeOffset Version);
