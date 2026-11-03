namespace AccountabiliBuddy.Core.Domain;

/// <summary>How a calendar gives its events a day plan category (ADR 58).</summary>
public enum CalendarRuleMode
{
    /// <summary>The calendar's events get no category from the calendar (the default).</summary>
    Off,

    /// <summary>An event whose title contains a keyword gets that keyword's category.</summary>
    Keywords,

    /// <summary>Every event of the calendar gets one category.</summary>
    EveryEvent,
}
