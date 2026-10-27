namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Everything one call to the own relay needs (ADR 46): its HTTPS address, the certificate pin (null for a relay with a
/// normal certificate), the relay group id and the bearer token, both from the group's <see cref="Core.Sync.SyncKey"/>.
/// The token is a secret: <see cref="ToString"/> never shows it, so it can't reach a log by accident.
/// </summary>
public sealed class RelayAccess
{
    /// <param name="baseAddress">The relay's address, ending in "/" (see <see cref="RelayAddress.TryNormalize"/>).</param>
    /// <param name="pin">The certificate's SHA-256 fingerprint in base64url, or null to check the certificate normally.</param>
    public RelayAccess(Uri baseAddress, string? pin, string groupId, string token)
    {
        BaseAddress = baseAddress;
        Pin = pin;
        GroupId = groupId;
        Token = token;
    }

    public Uri BaseAddress { get; }

    public string? Pin { get; }

    public string GroupId { get; }

    public string Token { get; }

    public override string ToString() => "relay access";
}
