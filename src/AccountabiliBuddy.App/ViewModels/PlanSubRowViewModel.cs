namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One line inside an expanded work part of the Plan timeline: a stretch of deep work, a calendar event, lunch or a comms
/// window; or the accent "Now" line (<see cref="IsNowLine"/>) between two of them.
/// </summary>
public sealed class PlanSubRowViewModel
{
    public string TimeText { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>On the right: "1h 10m done", "Until 5:00 PM", "Calendar", "1h".</summary>
    public string RightText { get; init; } = string.Empty;

    /// <summary>A calendar event (its title is drawn in ink; the rest are Muted).</summary>
    public bool IsEvent { get; init; }

    /// <summary>The row is the "Now · 1:38 PM" rule, not a part.</summary>
    public bool IsNowLine { get; init; }

    public bool IsRow => !IsNowLine;
}
