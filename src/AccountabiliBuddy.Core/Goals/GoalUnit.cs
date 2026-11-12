using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>What a goal's amounts are counted in (brief §5.1).</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalUnit>))]
public enum GoalUnit
{
    Minutes,
    Count,
    Lb,
    Kg,
    Steps,

    /// <summary>A habit's tick: 1 = done, 0 = not.</summary>
    YesNo,
}
