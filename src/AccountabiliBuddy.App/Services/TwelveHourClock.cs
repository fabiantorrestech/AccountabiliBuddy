using System.Globalization;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Times are always shown 12-hour with AM/PM (project convention), whatever the Windows region's clock: the app's
/// culture keeps the region's dates and numbers but its time patterns become "h:mm tt" and "h:mm:ss tt".
/// </summary>
public static class TwelveHourClock
{
    public static void Apply()
    {
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        DateTimeFormatInfo format = culture.DateTimeFormat;
        format.ShortTimePattern = "h:mm tt";
        format.LongTimePattern = "h:mm:ss tt";
        format.AMDesignator = "AM";
        format.PMDesignator = "PM";
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }
}
