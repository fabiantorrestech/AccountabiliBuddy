namespace AccountabiliBuddy.Core.Domain;

/// <summary>A place on Earth in degrees: latitude north positive, longitude east positive. Private data (ADR 49).</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}
