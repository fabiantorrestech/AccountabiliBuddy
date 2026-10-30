using System.Security.Cryptography;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A device's own ECDH P-256 key pair (ADR 54), made once and kept: the public half goes in the device's card
/// (<see cref="SyncDeviceCard.PublicKey"/>), the private half stays on the device (DPAPI on the PC; the phone seals it
/// with a Keystore key). Other devices wrap the group's new link code for it with <see cref="RekeyWrap"/>. A public key
/// is the 65-byte uncompressed point: 0x04, X (32 bytes), Y (32 bytes). The phone does the same (android/shared).
/// </summary>
public sealed class SyncDeviceKey : IDisposable
{
    /// <summary>The length of a public key: 0x04, X and Y.</summary>
    public const int PublicKeyBytes = 65;

    private const int CoordinateBytes = 32;
    private const byte UncompressedPoint = 0x04;

    private readonly ECDiffieHellman _key;

    private SyncDeviceKey(ECDiffieHellman key)
    {
        _key = key;
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        PublicKey = EncodePublicKey(parameters.Q);
    }

    /// <summary>This device's public key (65 bytes).</summary>
    public byte[] PublicKey { get; }

    /// <summary>The public key as standard base64, the form in the device card.</summary>
    public string PublicKeyBase64 => Convert.ToBase64String(PublicKey);

    public static SyncDeviceKey Generate()
    {
        return new SyncDeviceKey(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));
    }

    /// <returns>The key pair from <see cref="ExportPkcs8"/>'s bytes, or null when they aren't a P-256 private key.</returns>
    public static SyncDeviceKey? TryFromPkcs8(ReadOnlySpan<byte> pkcs8)
    {
        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return key.KeySize == 256 && key.ExportParameters(false).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value
                ? new SyncDeviceKey(key)
                : Reject(key);
        }
        catch (CryptographicException)
        {
            key.Dispose();
            return null;
        }
    }

    /// <summary>The private key as PKCS#8 bytes, for the caller to protect before storing.</summary>
    public byte[] ExportPkcs8()
    {
        return _key.ExportPkcs8PrivateKey();
    }

    /// <summary>
    /// ECDH with <paramref name="otherPublicKey"/>: the shared point's X coordinate (32 bytes), to be run through a key
    /// derivation before use.
    /// </summary>
    /// <returns>Null when <paramref name="otherPublicKey"/> isn't a valid P-256 point.</returns>
    public byte[]? DeriveSecret(ReadOnlySpan<byte> otherPublicKey)
    {
        ECDiffieHellman? other = ImportPublicKey(otherPublicKey);
        if (other is null)
        {
            return null;
        }

        using (other)
        {
            return _key.DeriveRawSecretAgreement(other.PublicKey);
        }
    }

    /// <summary>Whether <paramref name="publicKey"/> is 65 bytes that make a point on P-256.</summary>
    public static bool IsValidPublicKey(ReadOnlySpan<byte> publicKey)
    {
        using ECDiffieHellman? key = ImportPublicKey(publicKey);
        return key is not null;
    }

    public void Dispose()
    {
        _key.Dispose();
    }

    private static SyncDeviceKey? Reject(ECDiffieHellman key)
    {
        key.Dispose();
        return null;
    }

    private static ECDiffieHellman? ImportPublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != PublicKeyBytes || publicKey[0] != UncompressedPoint)
        {
            return null;
        }

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = publicKey.Slice(1, CoordinateBytes).ToArray(),
                Y = publicKey.Slice(1 + CoordinateBytes, CoordinateBytes).ToArray(),
            },
        };
        try
        {
            // Throws for a point that isn't on the curve.
            return ECDiffieHellman.Create(parameters);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[] EncodePublicKey(ECPoint point)
    {
        byte[] encoded = new byte[PublicKeyBytes];
        encoded[0] = UncompressedPoint;
        point.X!.CopyTo(encoded, 1);
        point.Y!.CopyTo(encoded, 1 + CoordinateBytes);
        return encoded;
    }
}
