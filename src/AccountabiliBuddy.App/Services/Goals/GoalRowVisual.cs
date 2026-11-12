namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>The small visual on a goal row, by kind (change note §4a "Row visual by kind").</summary>
public enum GoalRowVisual
{
    /// <summary>Nothing to draw (a goal that asks for nothing yet).</summary>
    None,

    /// <summary>Time and milestone goals: a progress bar.</summary>
    Bar,

    /// <summary>An amount to reach (weight): a sparkline of recent values.</summary>
    Sparkline,

    /// <summary>A daily amount (steps): 7 day bars, solid where met, today outlined.</summary>
    DayBars,

    /// <summary>A weekly-count habit: the week's dots, the week start first, today as a ring.</summary>
    WeekDots,

    /// <summary>A daily habit: the 14-day strip.</summary>
    Strip,
}
