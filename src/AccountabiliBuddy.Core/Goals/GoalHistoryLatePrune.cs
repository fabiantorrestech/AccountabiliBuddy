namespace AccountabiliBuddy.Core.Goals;

/// <summary>What the late joiner's rule needs (<see cref="GoalHistoryRules.LatePrunable"/>, the last <c>Merge</c> overload of <see cref="GoalHistoryRules"/>).</summary>
/// <param name="FirstDropAt">When the first merge that dropped a row of an unknown goal ran; null for none yet.</param>
/// <param name="Active">The active devices (<see cref="GoalHistoryRules.ActiveDevices"/>).</param>
/// <param name="Self">This device's id.</param>
/// <param name="LastRegularMerge">When each device's regular sync file (with goal parts) last merged here.</param>
public sealed record GoalHistoryLatePrune(
    DateTimeOffset? FirstDropAt, IReadOnlyList<GoalHistoryDevice> Active, Guid Self, IReadOnlyDictionary<Guid, DateTimeOffset> LastRegularMerge);
