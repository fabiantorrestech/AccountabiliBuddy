using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Options › Charts › Week starts on (change note §4a). Sunday by default.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalWeekStart>))]
public enum GoalWeekStart
{
    Sunday,
    Monday,
    Saturday,
}
