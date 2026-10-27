using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Seals and opens sync files (ADR 22). A sealed file is "ABS1", the key's 4-byte group id, a 12-byte random nonce, and
/// then AES-256-GCM over the raw-Deflated UTF-8 JSON of the <see cref="SyncFile"/> (camelCase,
/// <see cref="SyncJson.Options"/>), ending with the 16-byte tag. The phone writes and reads the same bytes
/// (android/shared).
/// </summary>
public static class SyncFileCodec
{
    private const int MagicBytes = 4;
    private const int GroupIdBytes = 4;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = MagicBytes + GroupIdBytes + NonceBytes;

    // Only a device holding the key can make a file that opens; this keeps a broken one from filling memory.
    private const int MaxJsonBytes = 16 * 1024 * 1024;

    private static readonly byte[] Magic = "ABS1"u8.ToArray();

    public static byte[] Seal(SyncFile file, SyncKey key)
    {
        byte[] compressed = Compress(file);
        byte[] output = new byte[HeaderBytes + compressed.Length + TagBytes];
        Magic.CopyTo(output, 0);
        Convert.FromHexString(key.GroupId).CopyTo(output, MagicBytes);
        Span<byte> nonce = output.AsSpan(MagicBytes + GroupIdBytes, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key.Key, TagBytes);
        aes.Encrypt(nonce, compressed, output.AsSpan(HeaderBytes, compressed.Length), output.AsSpan(HeaderBytes + compressed.Length, TagBytes));
        return output;
    }

    /// <returns>How many bytes <see cref="Seal"/> gives for the file (its nonce is random, its length isn't), without the key.</returns>
    public static int SealedLength(SyncFile file)
    {
        return HeaderBytes + Compress(file).Length + TagBytes;
    }

    /// <returns>
    /// The file, or null when it isn't one this group's devices wrote or can't be read: another group or key, a
    /// changed or cut-off file, a header other than "ABS1", JSON that isn't a complete sync file, or a format version
    /// newer than this app's.
    /// </returns>
    public static SyncFile? Open(ReadOnlySpan<byte> sealedBytes, SyncKey key)
    {
        if (sealedBytes.Length < HeaderBytes + TagBytes
            || !sealedBytes[..MagicBytes].SequenceEqual(Magic)
            || !sealedBytes.Slice(MagicBytes, GroupIdBytes).SequenceEqual(Convert.FromHexString(key.GroupId)))
        {
            return null;
        }

        ReadOnlySpan<byte> cipherText = sealedBytes[HeaderBytes..^TagBytes];
        byte[] compressed = new byte[cipherText.Length];
        try
        {
            using var aes = new AesGcm(key.Key, TagBytes);
            aes.Decrypt(sealedBytes.Slice(MagicBytes + GroupIdBytes, NonceBytes), cipherText, sealedBytes[^TagBytes..], compressed);
        }
        catch (CryptographicException)
        {
            // The tag doesn't match: another key, or the bytes were changed.
            return null;
        }

        byte[]? json = Inflate(compressed);
        if (json is null)
        {
            return null;
        }

        SyncFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SyncFile>(json, SyncJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }

        return file is not null && IsComplete(file) ? file : null;
    }

    /// <summary>
    /// Missing lists and fields come through as null from the records' constructors; a file needs them all, so later
    /// steps never meet a null. Unknown values (a kind, a method or a document key from a newer app) are kept.
    /// </summary>
    private static bool IsComplete(SyncFile file)
    {
        if (file.FormatVersion < 1 || file.FormatVersion > SyncFile.CurrentFormatVersion)
        {
            return false;
        }

        SyncDeviceCard? device = file.Device;
        return device is { Name: not null, Kind: not null, AppVersion: not null, Methods: not null, Seen: not null }
            && device.DeviceId != Guid.Empty
            && device.Methods.All(method => method is not null)
            && file.Sessions is not null
            && file.Sessions.All(session => session is not null)
            && file.Drift is not null
            && file.Drift.All(drift => drift is { App: not null })
            && (file.DayPlan is null or { PlanJson: not null, Version: not null })
            && file.Documents is not null
            && file.Documents.All(document => document is { Key: not null, Json: not null });
    }

    private static byte[] Compress(SyncFile file)
    {
        return Deflate(JsonSerializer.SerializeToUtf8Bytes(file, SyncJson.Options));
    }

    private static byte[] Deflate(byte[] json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            deflate.Write(json);
        }

        return output.ToArray();
    }

    /// <returns>The inflated bytes, or null when they aren't raw Deflate data or exceed <see cref="MaxJsonBytes"/>.</returns>
    private static byte[]? Inflate(byte[] compressed)
    {
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxJsonBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
