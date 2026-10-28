using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Windows.Devices.Geolocation;

namespace AccountabiliBuddy.Platform.Windows.Location;

/// <summary>
/// Windows location at coarse accuracy (ADR 49): enough for sunrise and sunset, and the least Windows has to work out. It
/// asks Windows for the position and opens no connection of its own; whatever Windows does to find it is Windows' own.
/// Gives up after a short wait.
/// </summary>
public sealed class WindowsLocationProvider : ILocationProvider
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(6);
    private const uint AccuracyMeters = 3000;

    public LocationAccess Access { get; private set; } = LocationAccess.Unknown;

    public async Task<GeoPoint?> GetApproximateAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            GeolocationAccessStatus status = await Geolocator.RequestAccessAsync().AsTask(timeout.Token);
            if (status != GeolocationAccessStatus.Allowed)
            {
                Access = LocationAccess.Denied;
                return null;
            }

            var geolocator = new Geolocator { DesiredAccuracyInMeters = AccuracyMeters };
            Geoposition position = await geolocator.GetGeopositionAsync(MaximumAge, Timeout).AsTask(timeout.Token);
            BasicGeoposition coordinate = position.Coordinate.Point.Position;
            Access = LocationAccess.Allowed;
            return new GeoPoint(coordinate.Latitude, coordinate.Longitude);
        }
        catch (UnauthorizedAccessException)
        {
            Access = LocationAccess.Denied;
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or COMException)
        {
            Access = LocationAccess.Unavailable;
            return null;
        }
    }
}
