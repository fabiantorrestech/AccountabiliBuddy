using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Which days a goal reminder rings on (brief §5.1).</summary>
[JsonConverter(typeof(GoalEnumConverter<GoalReminderDays>))]
public enum GoalReminderDays
{
    /// <summary>Today only (<see cref="GoalReminder.Date"/> holds the day it was set for).</summary>
    Today,

    /// <summary>The weekdays in <see cref="GoalReminder.Weekdays"/>.</summary>
    Weekdays,

    /// <summary>One date (<see cref="GoalReminder.Date"/>).</summary>
    Date,

    EveryDay,
}
