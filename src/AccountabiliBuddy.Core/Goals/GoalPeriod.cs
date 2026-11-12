using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>The span a goal's target is for (brief §5.1). Weeks start on the user's week start (<see cref="GoalWeekStart"/>).</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalPeriod>))]
public enum GoalPeriod
{
    Day,
    Week,

    /// <summary>Two weeks, counted in pairs from the week the goal was created in (<see cref="GoalPeriods"/>).</summary>
    TwoWeeks,

    /// <summary>A calendar month (the week start doesn't matter).</summary>
    Month,
}
