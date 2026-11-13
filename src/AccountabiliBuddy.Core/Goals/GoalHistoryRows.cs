namespace AccountabiliBuddy.Core.Goals;

/// <summary>One goal history file as read for <see cref="GoalHistoryRules.Merge"/>: the listed file (key and version) and its entry rows.</summary>
/// <param name="File">The file as listed when it was read (<see cref="GoalHistoryRules.ToRead"/>).</param>
/// <param name="Entries">Its entries (<see cref="GoalHistoryMonth.Entries"/> or <see cref="GoalHistoryYear.Entries"/>).</param>
public sealed record GoalHistoryRows(GoalHistoryListing File, IReadOnlyList<GoalEntry> Entries);
