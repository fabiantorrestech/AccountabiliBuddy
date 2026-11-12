using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// A day of the week as goal reminders store it: written by name ("Monday"), not as a number, so the phone's twin reads the
/// same text. The values follow <see cref="DayOfWeek"/>'s order, so <c>(DayOfWeek)weekday</c> converts.
/// </summary>
[JsonConverter(typeof(GoalEnumConverter<GoalWeekday>))]
public enum GoalWeekday
{
    Sunday,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
}
