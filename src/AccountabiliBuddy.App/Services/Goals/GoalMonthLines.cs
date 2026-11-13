using System.Text.Json.Serialization;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>
/// How strongly heatmaps draw the line between two months (round 2 HM1; Goals Options › Charts, Stats Options, Settings › Goals). One
/// device-local choice for every heatmap that shows month boundaries. The strengths are theme tokens (see <c>HeatmapControl</c> styles in
/// AbStyles.axaml): Lighter = Ab.Line, Normal = Ab.Faint (the look before HM1), Darker = Ab.Muted; Off draws no line (labels and spacing stay).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalMonthLines>))]
public enum GoalMonthLines
{
    /// <summary>The default: today's look.</summary>
    Normal,

    Darker,

    Lighter,

    Off,
}
