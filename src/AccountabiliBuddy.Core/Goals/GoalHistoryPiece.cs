namespace AccountabiliBuddy.Core.Goals;

/// <summary>A month file a year file took (<see cref="GoalHistoryYear.Months"/>): its device, month and the version (<see cref="GoalHistoryMonth.WrittenAt"/>).</summary>
public sealed record GoalHistoryPiece(Guid DeviceId, int Month, DateTimeOffset WrittenAt);
