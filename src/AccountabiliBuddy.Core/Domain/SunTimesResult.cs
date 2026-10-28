namespace AccountabiliBuddy.Core.Domain;

/// <summary>Sunrise and sunset of one day at one place, as moments in the local time zone.</summary>
public sealed record SunTimesResult(DateTimeOffset Sunrise, DateTimeOffset Sunset);
