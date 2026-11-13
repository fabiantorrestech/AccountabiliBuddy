namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A device of the sync group as this PC knows it from the files it received (<see cref="GoalHistorySync"/>).</summary>
/// <param name="ReceivedAt">When this PC last accepted a file from it (the trusted clock); null for never.</param>
public sealed record GoalHistoryDeviceInfo(Guid Id, bool IsPc, bool Removed, DateTimeOffset? ReceivedAt);
