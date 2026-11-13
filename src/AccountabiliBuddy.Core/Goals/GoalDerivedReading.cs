namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// One fresh derived day value this device measured for a goal (ADR 79, GS1b), handed to <see cref="GoalDerivedWriter"/>: a Health Connect
/// value, its own screen-time minutes, a PC's #tag extra (<see cref="GoalEntrySource.FocusLocal"/>) or its calendar extra. A real 0 is a
/// reading (the device measured nothing), not the absence of one.
/// </summary>
/// <param name="Source">A device-measured source (<see cref="GoalDerivedWriter.IsDerived"/>).</param>
/// <param name="Day">The day on this device's calendar; never re-bucketed by a receiver.</param>
/// <param name="Value">In the goal's unit (minutes, steps, lb or kg, hours of sleep).</param>
public sealed record GoalDerivedReading(Guid GoalId, GoalEntrySource Source, DateOnly Day, double Value);
