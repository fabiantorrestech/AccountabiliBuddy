using System.Text.Json.Serialization;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>Which goals the Goals list shows (brief §4.3, §4.4: "By area / Current / All").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalListView>))]
public enum GoalListView
{
    /// <summary>Live goals grouped under their areas (the default).</summary>
    ByArea,

    /// <summary>The goals that ask for something this period.</summary>
    Current,

    /// <summary>Every goal, the paused ones faded at the end.</summary>
    All,
}
