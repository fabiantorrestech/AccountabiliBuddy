using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One line inside an expanded work part of the Plan timeline: a stretch of deep work, a calendar event, lunch or a comms
/// window; or the accent "Now" line (<see cref="IsNowLine"/>) between two of them.
/// </summary>
public sealed partial class PlanSubRowViewModel
{
    private readonly Action<CalendarEvent>? _openEvent;

    /// <param name="openEvent">What clicking a timed calendar event row does (opens its picker); null for rows that take no click.</param>
    public PlanSubRowViewModel(Action<CalendarEvent>? openEvent = null)
    {
        _openEvent = openEvent;
    }

    /// <summary>The calendar event a timed event row stands for (its click opens the picker); null for the other rows.</summary>
    public CalendarEvent? Event { get; init; }

    /// <summary>A timed calendar event that can be clicked to pick a category.</summary>
    public bool IsClickable => IsEvent && Event is not null && _openEvent is not null;

    public bool IsPlainRow => IsRow && !IsClickable;

    [RelayCommand]
    private void Open()
    {
        if (Event is not null)
        {
            _openEvent?.Invoke(Event);
        }
    }

    public string TimeText { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>On the right: "1h 10m done", "Until 5:00 PM", "Calendar", "1h".</summary>
    public string RightText { get; init; } = string.Empty;

    /// <summary>A calendar event (its title is drawn in ink; the rest are Muted).</summary>
    public bool IsEvent { get; init; }

    /// <summary>Work ended early (ADR 65): drawn lighter.</summary>
    public bool IsEnded { get; init; }

    public double RowOpacity => IsEnded ? 0.45 : 1;

    /// <summary>The row is the "Now · 1:38 PM" rule, not a part.</summary>
    public bool IsNowLine { get; init; }

    public bool IsRow => !IsNowLine;
}
