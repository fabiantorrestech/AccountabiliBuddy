using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Where a goal entry came from (brief §5.1). Pixel Watch data arrives as <see cref="HealthConnect"/>.</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalEntrySource>))]
public enum GoalEntrySource
{
    /// <summary>Typed, ticked or added by hand: the user's own decision, which wins over any reading.</summary>
    Hand,

    HealthConnect,
    ScreenTime,
    Beeminder,
    Calendar,
}
