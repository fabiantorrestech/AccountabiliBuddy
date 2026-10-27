using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The sync group's key (ADR 22), derived from the link code's 200 random bits with HKDF-SHA256 (salt
/// "AccountabiliBuddy sync"): the 32-byte AES-GCM key (info "key") and a 4-byte group id (info "group") that tells one
/// group's files from another's without trying the key. Made from a code with <see cref="SyncKeyCode.TryParse"/>.
/// For the own relay (ADR 46) two more values come from the same secret, neither of which can be turned back into the
/// key: a relay group id (info "relay-group", 16 bytes, hex) and a bearer token (info "relay-auth", 32 bytes,
/// base64url). The phone derives them the same way (android/shared).
/// </summary>
public sealed class SyncKey
{
    /// <summary>The link code's 200 bits.</summary>
    public const int SecretBytes = 25;

    private const int KeyBytes = 32;
    private const int GroupIdBytes = 4;
    private const int RelayGroupIdBytes = 16;
    private const int RelayTokenBytes = 32;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("AccountabiliBuddy sync");
    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("key");
    private static readonly byte[] GroupInfo = Encoding.UTF8.GetBytes("group");
    private static readonly byte[] RelayGroupInfo = Encoding.UTF8.GetBytes("relay-group");
    private static readonly byte[] RelayAuthInfo = Encoding.UTF8.GetBytes("relay-auth");

    private SyncKey(byte[] key, string groupId, string relayGroupId, string relayToken)
    {
        Key = key;
        GroupId = groupId;
        RelayGroupId = relayGroupId;
        RelayToken = relayToken;
    }

    /// <summary>The AES-256-GCM key (32 bytes).</summary>
    public byte[] Key { get; }

    /// <summary>The group id as 8 lower-case hex characters; its 4 bytes start every sealed file after "ABS1".</summary>
    public string GroupId { get; }

    /// <summary>The group's id on a relay: 32 lower-case hex characters (16 bytes). Names the group in the relay's addresses.</summary>
    public string RelayGroupId { get; }

    /// <summary>
    /// The bearer token that proves a device belongs to the group on a relay: 43 base64url characters (32 bytes, no
    /// padding). A secret of the group: sent only in the Authorization header over HTTPS, never logged or put in an address.
    /// </summary>
    public string RelayToken { get; }

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
        byte[] relayGroupId = new byte[RelayGroupIdBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret25, relayGroupId, Salt, RelayGroupInfo);
        byte[] relayToken = new byte[RelayTokenBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret25, relayToken, Salt, RelayAuthInfo);
        return new SyncKey(key, Convert.ToHexStringLower(groupId), Convert.ToHexStringLower(relayGroupId), Base64Url(relayToken));
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
