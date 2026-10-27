using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The sync group's key (ADR 22), derived from the link code's 200 random bits with HKDF-SHA256 (salt
/// "AccountabiliBuddy sync"): the 32-byte AES-GCM key (info "key") and a 4-byte group id (info "group") that tells one
/// group's files from another's without trying the key. Made from a code with <see cref="SyncKeyCode.TryParse"/>.
/// For the own relay (ADR 46) the same secret gives a third value, a relay root (info "relay-root", 32 bytes), from which
/// each relay gets ITS OWN group id and bearer token, named by the relay's identity (its pin, else its address): a relay that
/// learns its token can't use it on another relay, and two relays can't tell they hold the same group. None of them can
/// be turned back into the key. The phone derives them the same way (android/shared).
/// </summary>
public sealed class SyncKey
{
    /// <summary>The link code's 200 bits.</summary>
    public const int SecretBytes = 25;

    private const int KeyBytes = 32;
    private const int GroupIdBytes = 4;
    private const int RelayRootBytes = 32;
    private const int RelayGroupIdBytes = 16;
    private const int RelayTokenBytes = 32;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("AccountabiliBuddy sync");
    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("key");
    private static readonly byte[] GroupInfo = Encoding.UTF8.GetBytes("group");
    private static readonly byte[] RelayRootInfo = Encoding.UTF8.GetBytes("relay-root");

    private readonly byte[] _relayRoot;

    private SyncKey(byte[] key, string groupId, byte[] relayRoot)
    {
        Key = key;
        GroupId = groupId;
        _relayRoot = relayRoot;
    }

    /// <summary>The AES-256-GCM key (32 bytes).</summary>
    public byte[] Key { get; }

    /// <summary>The group id as 8 lower-case hex characters; its 4 bytes start every sealed file after "ABS1".</summary>
    public string GroupId { get; }

    /// <summary>
    /// The group's id on the relay named <paramref name="relayIdentity"/>: 32 lower-case hex characters (16 bytes). Names the group in
    /// the relay's addresses.
    /// </summary>
    /// <param name="relayIdentity">What names the relay, from <see cref="RelayIdentity.Of"/>.</param>
    public string RelayGroupIdFor(string relayIdentity)
    {
        byte[] bytes = new byte[RelayGroupIdBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _relayRoot, bytes, Salt, Encoding.UTF8.GetBytes("relay-group|" + relayIdentity));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// The bearer token that proves a device belongs to the group on the relay named <paramref name="relayIdentity"/>: 43 base64url
    /// characters (32 bytes, no padding). A secret of the group on that relay: sent only in the Authorization header over
    /// HTTPS, never logged or put in an address.
    /// </summary>
    /// <param name="relayIdentity">What names the relay, from <see cref="RelayIdentity.Of"/>.</param>
    public string RelayTokenFor(string relayIdentity)
    {
        byte[] bytes = new byte[RelayTokenBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _relayRoot, bytes, Salt, Encoding.UTF8.GetBytes("relay-auth|" + relayIdentity));
        return Base64Url(bytes);
    }

    /// <summary>
    /// Whether <paramref name="other"/> is the same group: compares the full 256-bit key in constant time. The 4-byte group id is
    /// published in every sealed file, so a crafted code can share it; never decide "same group" on it.
    /// </summary>
    public bool SameGroupAs(SyncKey? other)
    {
        return other is not null && CryptographicOperations.FixedTimeEquals(Key, other.Key);
    }

    /// <summary>The same group's key, as a hex SHA-256 of the key: tells groups apart in memory without holding the key's text.</summary>
    public string Fingerprint()
    {
        return Convert.ToHexStringLower(SHA256.HashData(Key));
    }

    /// <exception cref="ArgumentException">The secret isn't <see cref="SecretBytes"/> bytes.</exception>
    public static SyncKey FromSecret(ReadOnlySpan<byte> secret25)
    {
        if (secret25.Length != SecretBytes)
        {
            throw new ArgumentException($"The link code's secret is {SecretBytes} bytes.", nameof(secret25));
        }

        byte[] key = new byte[KeyBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret25, key, Salt, KeyInfo);
        byte[] groupId = new byte[GroupIdBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret25, groupId, Salt, GroupInfo);
        byte[] relayRoot = new byte[RelayRootBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret25, relayRoot, Salt, RelayRootInfo);
        return new SyncKey(key, Convert.ToHexStringLower(groupId), relayRoot);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
