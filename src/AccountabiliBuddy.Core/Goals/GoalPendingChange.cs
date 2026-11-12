using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A looser target waiting for its delay (brief §5.7; <see cref="GoalLowering"/>): shown on the Target tile ("Lowers to 1h 30m at
/// 2:14 PM") and in Edit goal, with Cancel. It applies once <see cref="AppliesAt"/> has passed and <see cref="GoalLowering.CanApply"/>
/// says so (never during a session outside ProductaBuddy mode).
/// </summary>
/// <param name="Amount">The new amount; null = no target any more ("Track time only").</param>
/// <param name="AppliesAt">The earliest it may apply.</param>
/// <param name="MadeInProductaBuddy">
/// Made in ProductaBuddy mode, where it skipped the delay: in AccountabiliBuddy mode it waits the full delay first
/// (<see cref="GoalLowering.IntoAccountabiliBuddy"/>). Left out of the JSON when false, so older stored changes read as made in
/// AccountabiliBuddy mode.
/// </param>
public sealed record GoalPendingChange(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Amount,
    DateTimeOffset AppliesAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool MadeInProductaBuddy = false);
