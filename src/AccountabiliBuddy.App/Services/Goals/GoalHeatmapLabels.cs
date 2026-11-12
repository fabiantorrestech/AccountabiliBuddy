using System.Text.Json.Serialization;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>The heatmap's day labels (Goals Options › Charts, brief §4.3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalHeatmapLabels>))]
public enum GoalHeatmapLabels
{
    All,

    /// <summary>Every other day (the default; the brief names no default, Q373).</summary>
    Some,

    None,
}
