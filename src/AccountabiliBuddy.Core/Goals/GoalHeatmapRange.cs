using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>The goal heatmap's range buttons (change note §4a: 1 month / 3 months / 6 months / Year), shortest first.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalHeatmapRange>))]
public enum GoalHeatmapRange
{
    OneMonth,
    ThreeMonths,
    SixMonths,
    Year,
}
