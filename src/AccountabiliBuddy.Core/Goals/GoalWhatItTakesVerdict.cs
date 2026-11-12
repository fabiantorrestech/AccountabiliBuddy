using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>The "What it takes" pop-up's verdict (brief §5.6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalWhatItTakesVerdict>))]
public enum GoalWhatItTakesVerdict
{
    /// <summary>The target is already reached.</summary>
    Met,

    /// <summary>Green: "Your daily goal already covers it".</summary>
    Covered,

    /// <summary>Amber: how much more is needed per day and per week.</summary>
    NeedsMore,

    /// <summary>"The date has passed", with Change the target.</summary>
    DatePassed,

    /// <summary>No date: the remaining amount, the pace and the projected finish only.</summary>
    NoDate,
}
