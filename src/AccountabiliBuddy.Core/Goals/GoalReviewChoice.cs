using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>A weekly review's decision for a goal, and what the review suggests (brief §5.8).</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalReviewChoice>))]
public enum GoalReviewChoice
{
    KeepGoing,

    /// <summary>Adjust the target: opens Edit goal › Target with the suggested numbers; a lowering still waits.</summary>
    Adjust,

    Pause,

    /// <summary>"Decide next week": skips the goal in this review (round 2 R5b2; nothing changes, next week's review asks again).</summary>
    DecideNextWeek,
}
