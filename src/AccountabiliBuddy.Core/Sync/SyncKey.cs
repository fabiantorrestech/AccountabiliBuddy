using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The sync group's key (ADR 22), derived from the link code's 200 random bits with HKDF-SHA256 (salt
/// "AccountabiliBuddy sync"): the 32-byte AES-GCM key (info "key") and a 4-byte group id (info "group") that tells one
/// group's files from another's without trying the key. Made from a code with <see cref="SyncKeyCode.TryParse"/>.
/// </summary>
public sealed class SyncKey
{
    /// <summary>The link code's 200 bits.</summary>
    public const int SecretBytes = 25;

    private const int KeyBytes = 32;
    private const int GroupIdBytes = 4;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("AccountabiliBuddy sync");
    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("key");
    private static readonly byte[] GroupInfo = Encoding.UTF8.GetBytes("group");

    private SyncKey(byte[] key, string groupId)
    {
        Key = key;
        GroupId = groupId;
    }

    /// <summary>The AES-256-GCM key (32 bytes).</summary>
    public byte[] Key { get; }

    /// <summary>The group id as 8 lower-case hex characters; its 4 bytes start every sealed file after "ABS1".</summary>
    public string GroupId { get; }

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
        return new SyncKey(key, Convert.ToHexStringLower(groupId));
    }
}
