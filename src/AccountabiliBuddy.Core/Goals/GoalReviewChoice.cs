using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>A weekly review's decision for a goal, and what the review suggests (brief §5.8).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalReviewChoice>))]
public enum GoalReviewChoice
{
    KeepGoing,

    /// <summary>Adjust the target: opens Edit goal › Target with the suggested numbers; a lowering still waits.</summary>
    Adjust,

    Pause,
}
