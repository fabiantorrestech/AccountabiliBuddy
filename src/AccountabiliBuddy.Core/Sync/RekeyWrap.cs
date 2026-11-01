using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Wraps the new link code's 25-byte secret for one device (ADR 54), so only that device can read it. The phone does the
/// same (android/shared, RekeyWrap.kt) and the two must stay byte for byte alike:
/// <list type="number">
/// <item>The wrapper makes a one-time ECDH P-256 key pair and takes the ECDH shared secret with the recipient's public key
/// (the X coordinate, 32 bytes).</item>
/// <item>The AES-256-GCM key is HKDF-SHA256 over that secret, salt "AccountabiliBuddy rekey" (UTF-8), info = "wrap"
/// (ASCII) + the recipient's 16 id bytes + the 4 bytes of the old group id; 32 bytes.</item>
/// <item>The secret is encrypted with a random 12-byte nonce; the associated data is the same info bytes; the 16-byte tag
/// follows the ciphertext (41 bytes in all).</item>
/// </list>
/// A device id's 16 bytes are the GUID in RFC 4122 order (the order of its text, <c>Guid.ToByteArray(bigEndian: true)</c>).
/// </summary>
public static class RekeyWrap
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("AccountabiliBuddy rekey");
    private static readonly byte[] InfoPrefix = Encoding.ASCII.GetBytes("wrap");

    /// <summary>The length of <see cref="RekeyWrapRecord.Wrapped"/>: the secret and the tag.</summary>
    public const int WrappedBytes = SyncKey.SecretBytes + TagBytes;

    /// <summary>The length of <see cref="RekeyWrapRecord.Nonce"/>.</summary>
    public const int NonceLength = NonceBytes;

    /// <returns>The secret wrapped for the recipient, or null when its public key isn't a valid P-256 point.</returns>
    /// <param name="secret">The new link code's 25 bytes.</param>
    /// <param name="oldGroupId">The group id (8 hex characters) of the key the rekey file is sealed with.</param>
    public static RekeyWrapRecord? Create(ReadOnlySpan<byte> secret, Guid recipientId, ReadOnlySpan<byte> recipientPublicKey, string oldGroupId)
    {
        if (secret.Length != SyncKey.SecretBytes)
        {
            throw new ArgumentException($"The secret is {SyncKey.SecretBytes} bytes.", nameof(secret));
        }

        using var ephemeral = SyncDeviceKey.Generate();
        byte[]? shared = ephemeral.DeriveSecret(recipientPublicKey);
        if (shared is null)
        {
            return null;
        }

        byte[] info = InfoFor(recipientId, oldGroupId);
        byte[] key = DeriveKey(shared, info);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] wrapped = new byte[WrappedBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, secret, wrapped.AsSpan(0, SyncKey.SecretBytes), wrapped.AsSpan(SyncKey.SecretBytes, TagBytes), info);
            return new RekeyWrapRecord(recipientId, ephemeral.PublicKey, nonce, wrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <returns>The 25-byte secret, or null when the record is malformed or wasn't wrapped for this device's key and group.</returns>
    public static byte[]? TryUnwrap(RekeyWrapRecord record, SyncDeviceKey ownKey, string oldGroupId)
    {
        if (record.Nonce.Length != NonceBytes || record.Wrapped.Length != WrappedBytes)
        {
            return null;
        }

        byte[]? shared = ownKey.DeriveSecret(record.EphemeralPublicKey);
        if (shared is null)
        {
            return null;
        }

        byte[] info = InfoFor(record.DeviceId, oldGroupId);
        byte[] key = DeriveKey(shared, info);
        byte[] secret = new byte[SyncKey.SecretBytes];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(record.Nonce, record.Wrapped.AsSpan(0, SyncKey.SecretBytes), record.Wrapped.AsSpan(SyncKey.SecretBytes, TagBytes), secret, info);
            return secret;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(secret);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] InfoFor(Guid recipientId, string oldGroupId)
    {
        byte[] info = new byte[InfoPrefix.Length + 16 + 4];
        InfoPrefix.CopyTo(info, 0);
        recipientId.TryWriteBytes(info.AsSpan(InfoPrefix.Length, 16), bigEndian: true, out _);
        Convert.FromHexString(oldGroupId).CopyTo(info, InfoPrefix.Length + 16);
        return info;
    }

    private static byte[] DeriveKey(byte[] shared, byte[] info)
    {
        byte[] key = new byte[KeyBytes];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, key, Salt, info);
        return key;
    }
}
