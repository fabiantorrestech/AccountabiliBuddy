namespace AccountabiliBuddy.App.Services;

/// <summary>Where a <see cref="CalendarSource"/>'s events come from.</summary>
public enum CalendarSourceKind
{
    /// <summary>Events added in the app (one built-in source).</summary>
    Local,

    /// <summary>A read-only iCal (.ics) address (ADR 9).</summary>
    ICalLink,

    /// <summary>A Google account connected with sign-in (ADR 9).</summary>
    Google,
}
