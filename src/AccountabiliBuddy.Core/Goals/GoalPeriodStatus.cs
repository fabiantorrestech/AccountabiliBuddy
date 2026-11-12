using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>"This period" for a goal, and the Target tile's pill (brief §5.5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalPeriodStatus>))]
public enum GoalPeriodStatus
{
    OnTrack,
    Behind,

    /// <summary>The period hasn't asked for anything yet (nothing done, and the slack to wait).</summary>
    NotDueYet,

    /// <summary>The goal is paused today: left out of the This week card.</summary>
    Paused,
}
