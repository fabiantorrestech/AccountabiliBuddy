namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// The add box's help for a typed title (spec 05 section 6): the counter, the project marker and the split suggestion. Never blocks adding.
/// </summary>
/// <param name="Length">The title's length in characters.</param>
/// <param name="ShowCounter">The counter shows (from <see cref="TaskTitleHelp.CounterFrom"/> characters).</param>
/// <param name="OverSoftLimit">The title is longer than <see cref="TaskTitleHelp.SoftLimit"/>: the hint line offers the split.</param>
/// <param name="Marker">A marker at the start; null when none (or the user kept the title as typed).</param>
/// <param name="Split">A split suggestion; null when the title is short, has no break or was kept as typed.</param>
public sealed record TaskTitleAnalysis(int Length, bool ShowCounter, bool OverSoftLimit, TaskProjectMarker? Marker, TaskTitleSplit? Split);
