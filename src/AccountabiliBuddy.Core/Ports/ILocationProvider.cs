using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// This computer's approximate location (coarse, a few kilometres), for sunrise and sunset only (ADR 49). The adapter makes
/// no network connection of its own. The caller decides how often to ask (at most once a day) and keeps the result private.
/// </summary>
public interface ILocationProvider
{
    /// <summary>What the last <see cref="GetApproximateAsync"/> found out about permission.</summary>
    LocationAccess Access { get; }

    /// <returns>The approximate position, or null when location is off, not available or takes too long (see <see cref="Access"/>).</returns>
    Task<GeoPoint?> GetApproximateAsync(CancellationToken cancellationToken);
}
