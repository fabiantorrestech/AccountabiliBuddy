using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the Plan timeline: when, a 6px bar (sleep, wake and bedtime in the sleep colour), a title and a Muted line; a work part also lists its sub-rows.</summary>
public sealed partial class PlanTimelineRowViewModel
{
    private readonly Action<PlanTimelineRowViewModel> _open;

    public PlanTimelineRowViewModel(Action<PlanTimelineRowViewModel> open)
    {
        _open = open;
    }

    public string TimeText { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string SubText { get; init; } = string.Empty;

    /// <summary>Sleep, wake and bedtime rows use the sleep colour; the others the plan colour.</summary>
    public bool IsSleep { get; init; }

    public IReadOnlyList<PlanSubRowViewModel> SubRows { get; init; } = Array.Empty<PlanSubRowViewModel>();

    public bool HasSubRows => SubRows.Count > 0;

    /// <summary>The plan part that stands for this row, so its editor can open on it; null for a part the editor doesn't list (a Budget day's window).</summary>
    public Guid? PartId { get; init; }

    /// <summary>A calendar event's own row: an all-day one at the top of the day opens the month view; a timed one outside a work block opens the event menu.</summary>
    public bool IsEvent { get; init; }

    /// <summary>The calendar event this row stands for; null for a part of the plan (ADR 64).</summary>
    public CalendarEvent? Event { get; init; }

    /// <summary>When the row's time is; orders a timed event's row among the parts. Null for all-day rows.</summary>
    public DateTime? At { get; init; }

    public string Tip => IsEvent ? "Right-click for this event's options" : "Open this part in its editor";

    /// <summary>The part was made by a classified calendar event (ADR 58): it exists only in the effective plan, so its click opens the event's picker.</summary>
    public bool FromEvent { get; init; }

    /// <summary>The category and hours of the part that stands for this row, to find the event or the stored part it came from; null when it has none.</summary>
    public Guid? CategoryId { get; init; }

    public string? PartLabel { get; init; }

    public LocalWindow? PartWindow { get; init; }

    [RelayCommand]
    private void Open() => _open(this);
}
