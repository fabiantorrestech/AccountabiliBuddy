using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Everything one call to the own relay needs (ADR 46): its HTTPS address, the certificate pin (null for a relay with a
/// normal certificate), the relay group id and the bearer token, both derived for THIS relay from the group's
/// <see cref="SyncKey"/> (see <see cref="For"/>), and the relay's admission key when this PC has one (it lets the
/// PC start the group on the relay). The token and the key are secrets: <see cref="ToString"/> never shows them, so
/// they can't reach a log by accident.
/// </summary>
public sealed class RelayAccess
{
    /// <param name="baseAddress">The relay's address, ending in "/" (see <see cref="RelayAddress.TryNormalize"/>).</param>
    /// <param name="pin">The relay's certificate SHA-256 fingerprint in base64url, or null to check the certificate normally.</param>
    /// <param name="admissionKey">The relay's admission key, or null when this PC doesn't have one.</param>
    public RelayAccess(Uri baseAddress, string? pin, string groupId, string token, string? admissionKey = null)
    {
        BaseAddress = baseAddress;
        Pin = pin;
        GroupId = groupId;
        Token = token;
        AdmissionKey = string.IsNullOrWhiteSpace(admissionKey) ? null : admissionKey.Trim();
    }

    public Uri BaseAddress { get; }

    public string? Pin { get; }

    public string GroupId { get; }

    public string Token { get; }

    /// <summary>Sent only with the call that creates the group on the relay (the first PUT); null when the PC has none.</summary>
    public string? AdmissionKey { get; }

    /// <summary>The access of <paramref name="key"/>'s group on the relay at <paramref name="baseAddress"/> (its id and token are that relay's own).</summary>
    public static RelayAccess For(SyncKey key, Uri baseAddress, string? pin, string? admissionKey)
    {
        string relay = RelayIdentity.Of(baseAddress, pin);
        return new RelayAccess(baseAddress, pin, key.RelayGroupIdFor(relay), key.RelayTokenFor(relay), admissionKey);
    }

    public override string ToString() => "relay access";
}
