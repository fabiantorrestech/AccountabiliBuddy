using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>Everything <see cref="CalendarDayOverlay"/> needs besides the stored plan (ADR 58).</summary>
/// <param name="Events">The events of the calendars the user can see.</param>
/// <param name="Classifications">The categories chosen on events and series.</param>
/// <param name="Rules">The calendar rules in force now.</param>
/// <param name="FirstKnown">When each classified event was first seen at its current time and category.</param>
/// <param name="Now">The trusted clock's time.</param>
/// <param name="Zone">The time zone event times are in.</param>
/// <param name="SessionRunning">A focus session the user started is running: nothing loosens then.</param>
/// <param name="LooseningDelay">How long an event must be known before it starts to loosen the day.</param>
/// <param name="NightFallback">The night before a date from the sleep settings, as <see cref="DayWorkWindows"/> takes it.</param>
/// <param name="CountedFocus">The stretches of counted focus (a Budget counts them), the running session's span included; a Work event's Budget credit leaves them out so no time counts twice. Null = none.</param>
public sealed record CalendarOverlayInput(
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<EventClassification> Classifications,
    CalendarRuleSet Rules,
    IReadOnlyList<FirstKnownEntry> FirstKnown,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    bool SessionRunning,
    TimeSpan LooseningDelay,
    Func<DateOnly, SleepTimes?>? NightFallback = null,
    IReadOnlyList<LocalWindow>? CountedFocus = null);
