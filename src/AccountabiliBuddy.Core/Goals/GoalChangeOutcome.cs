using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>What happens to a target change (<see cref="GoalLowering.Decide"/>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalChangeOutcome>))]
public enum GoalChangeOutcome
{
    /// <summary>The same amount: nothing to do.</summary>
    Unchanged,

    /// <summary>Applies at once (stricter, or a goal that feeds no guard).</summary>
    ApplyNow,

    /// <summary>Kept as a <see cref="GoalPendingChange"/> until <see cref="GoalLowering.CanApply"/> says so.</summary>
    Wait,
}
