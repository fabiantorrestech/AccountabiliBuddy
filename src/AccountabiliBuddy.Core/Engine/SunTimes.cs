using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Sunrise and sunset from the NOAA solar calculator formulas (accurate to about a minute away from the poles). Pure: a date,
/// a place and a time zone in, moments out. Nothing here looks anything up.
/// </summary>
public static class SunTimes
{
    // The sun's centre 90.833 degrees from straight up: its edge on the horizon, plus atmospheric refraction.
    private const double SunriseZenithDegrees = 90.833;

    /// <returns>The day's sunrise and sunset, or null when the sun does not rise or set that day (polar day or night).</returns>
    public static SunTimesResult? Calculate(DateOnly date, GeoPoint point, TimeZoneInfo zone)
    {
        if (!point.IsValid)
        {
            return null;
        }

        var midnightUtc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        double latitude = ToRadians(point.Latitude);

        // Solar noon in minutes after 00:00 UTC of that date. The sun's position at noon decides the day's length, so work
        // out the position at the estimated noon, correct the noon with it, and repeat once.
        double noonMinutes = 720 - 4 * point.Longitude;
        SolarPosition position = PositionAt(midnightUtc.AddMinutes(noonMinutes));
        noonMinutes = 720 - 4 * point.Longitude - position.EquationOfTimeMinutes;
        position = PositionAt(midnightUtc.AddMinutes(noonMinutes));
        noonMinutes = 720 - 4 * point.Longitude - position.EquationOfTimeMinutes;

        double cosHourAngle = Math.Cos(ToRadians(SunriseZenithDegrees)) / (Math.Cos(latitude) * Math.Cos(position.Declination))
            - Math.Tan(latitude) * Math.Tan(position.Declination);
        if (double.IsNaN(cosHourAngle) || cosHourAngle > 1 || cosHourAngle < -1)
        {
            return null;
        }

        double hourAngleDegrees = ToDegrees(Math.Acos(cosHourAngle));
        return new SunTimesResult(
            ToLocal(midnightUtc.AddMinutes(noonMinutes - 4 * hourAngleDegrees), zone),
            ToLocal(midnightUtc.AddMinutes(noonMinutes + 4 * hourAngleDegrees), zone));
    }

    private static SolarPosition PositionAt(DateTime utc)
    {
        double julianDay = (utc - DateTime.UnixEpoch).TotalDays + 2440587.5;
        double t = (julianDay - 2451545.0) / 36525.0;

        double meanLongitude = Normalize(280.46646 + t * (36000.76983 + t * 0.0003032), 360);
        double meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double meanAnomalyRadians = ToRadians(meanAnomaly);

        double equationOfCentre = Math.Sin(meanAnomalyRadians) * (1.914602 - t * (0.004817 + 0.000014 * t))
            + Math.Sin(2 * meanAnomalyRadians) * (0.019993 - 0.000101 * t)
            + Math.Sin(3 * meanAnomalyRadians) * 0.000289;
        double trueLongitude = meanLongitude + equationOfCentre;

        double omega = 125.04 - 1934.136 * t;
        double apparentLongitude = trueLongitude - 0.00569 - 0.00478 * Math.Sin(ToRadians(omega));

        double meanObliquity = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
        double obliquity = ToRadians(meanObliquity + 0.00256 * Math.Cos(ToRadians(omega)));

        double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(ToRadians(apparentLongitude)));

        double y = Math.Pow(Math.Tan(obliquity / 2), 2);
        double meanLongitudeRadians = ToRadians(meanLongitude);
        double equationOfTime = y * Math.Sin(2 * meanLongitudeRadians)
            - 2 * eccentricity * Math.Sin(meanAnomalyRadians)
            + 4 * eccentricity * y * Math.Sin(meanAnomalyRadians) * Math.Cos(2 * meanLongitudeRadians)
            - 0.5 * y * y * Math.Sin(4 * meanLongitudeRadians)
            - 1.25 * eccentricity * eccentricity * Math.Sin(2 * meanAnomalyRadians);

        return new SolarPosition(declination, 4 * ToDegrees(equationOfTime));
    }

    private static DateTimeOffset ToLocal(DateTime utc, TimeZoneInfo zone)
    {
        var instant = new DateTimeOffset(utc, TimeSpan.Zero);
        return instant.ToOffset(zone.GetUtcOffset(instant));
    }

    private static double Normalize(double value, double modulus)
    {
        double result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;

    private readonly record struct SolarPosition(double Declination, double EquationOfTimeMinutes);
}
