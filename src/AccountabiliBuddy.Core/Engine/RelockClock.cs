namespace AccountabiliBuddy.Core.Engine;

/// <summary>Works out local relock moments for unlocks.</summary>
public static class RelockClock
{
    /// <summary>The first local midnight after <paramref name="after"/>.</summary>
    public static DateTimeOffset NextMidnight(DateTimeOffset after, TimeZoneInfo timeZone)
    {
        DateTime localMidnight = TimeZoneInfo.ConvertTime(after, timeZone).Date.AddDays(1);
        return new DateTimeOffset(localMidnight, timeZone.GetUtcOffset(localMidnight));
    }

    /// <summary>The local date <paramref name="moment"/> falls on.</summary>
    public static DateOnly LocalDate(DateTimeOffset moment, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);
    }
}
