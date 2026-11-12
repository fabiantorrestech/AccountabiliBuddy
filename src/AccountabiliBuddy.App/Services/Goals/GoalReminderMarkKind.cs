using System.Text.Json.Serialization;

namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>What happened to one occurrence of a goal reminder on this PC (round 2 R5.5; "goals.reminderState").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalReminderMarkKind>))]
public enum GoalReminderMarkKind
{
    /// <summary>Snoozed: rings again at <see cref="GoalReminderMark.SnoozedUntil"/>.</summary>
    Snoozed,

    /// <summary>Dismissed, or handled through the card's Tick / Log / Open goal.</summary>
    Dismissed,

    /// <summary>"Skip when the goal is already done": the goal was done when it came due, so it didn't ring.</summary>
    Skipped,

    /// <summary>It rang until its ring time ran out with nobody acting.</summary>
    RangOut,
}
