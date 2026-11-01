using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// Seals and opens the rekey file (ADR 54): what a device writes when it removes devices from the group. The phone writes
/// and reads the same bytes (android/shared, RekeyCodec.kt); keep the two identical.
/// <code>
/// file    = "ABR1" (4 ASCII bytes)
///           old group id (4 bytes, the group id of the key that seals it, as in "ABS1" files)
///           nonce (12 random bytes)
///           ciphertext (AES-256-GCM with the OLD group key; the associated data is the first 8 bytes of the file)
///           tag (16 bytes)
/// payload = version (1 byte, 1)
///           epoch (8 bytes, signed, big-endian: seconds since 1970-01-01 UTC)
///           remover: id (16 bytes, RFC 4122 order), name length (2 bytes, big-endian), name (UTF-8, at most 200 bytes)
///           removed count (2 bytes, big-endian), then for each removed device
///               id (16 bytes, RFC 4122 order), name length (2 bytes, big-endian), name (UTF-8, at most 200 bytes),
///               erase (1 byte: 1 = every staying device forgets what it received from this device, 0 = it is kept; any other value makes the file unreadable)
///           wrap count (2 bytes, big-endian), then for each remaining device
///               id (16 bytes), ephemeral public key (65 bytes), nonce (12 bytes), wrapped secret (41 bytes), see <see cref="RekeyWrap"/>
/// </code>
/// The old key opens it, so a removed device that still holds that key can read that it was removed; only a device with
/// a wrap for its own id and key can read the new link code (the maker is among the recipients, so it reads its own file the
/// same way). The file is named <see cref="FileNameFor"/> and goes where
/// the group's sync files go, so every method that carries them carries it.
/// </summary>
public static class RekeyCodec
{
    private const int MagicBytes = 4;
    private const int GroupIdBytes = 4;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = MagicBytes + GroupIdBytes + NonceBytes;
    private const int IdBytes = 16;
    private const byte Version = 1;
    private const int MaxNameBytes = 200;

    private static readonly byte[] Magic = "ABR1"u8.ToArray();

    /// <summary>The longest name <see cref="Seal"/> writes for a removed device, in UTF-8 bytes; a longer one is cut.</summary>
    public const int MaxNameLength = MaxNameBytes;

    /// <returns>Whether the bytes start like a rekey file ("ABR1"); it may still not open.</returns>
    public static bool IsRekey(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= HeaderBytes + TagBytes && bytes[..MagicBytes].SequenceEqual(Magic);
    }

    /// <returns>The group id (8 lower-case hex characters) a rekey file is sealed for, or null when it isn't one.</returns>
    public static string? GroupIdOf(ReadOnlySpan<byte> bytes)
    {
        return IsRekey(bytes) ? Convert.ToHexStringLower(bytes.Slice(MagicBytes, GroupIdBytes)) : null;
    }

    /// <summary>
    /// The name of the rekey file that <paramref name="makerId"/> writes for the group <paramref name="oldGroupId"/>: the first
    /// 6 bytes of SHA-256 over the UTF-8 of the group id, the maker's id ("D" format, lower case) and "rekey", as 12 lower-case
    /// hex characters, then ".absync". One file per maker, so two devices that remove at the same time never overwrite each
    /// other. Shaped like a device's file name (<c>SyncFolderService.FileNameFor</c>, whose hash input ends in a GUID, so the
    /// two never meet) so every method and the relay accept it.
    /// </summary>
    public static string FileNameFor(string oldGroupId, Guid makerId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(oldGroupId + makerId.ToString("D") + "rekey"));
        return Convert.ToHexStringLower(hash.AsSpan(0, 6)) + ".absync";
    }

    /// <summary>
    /// Which of two rekeys for the same group wins: the one whose maker has the lower id, comparing the ids' bytes (RFC 4122
    /// order, as in the file) as unsigned numbers. Every device picks the same one, whatever order it saw them in.
    /// </summary>
    /// <returns>Negative when <paramref name="first"/> wins, positive when <paramref name="second"/> does.</returns>
    public static int CompareMakers(Guid first, Guid second)
    {
        Span<byte> a = stackalloc byte[IdBytes];
        Span<byte> b = stackalloc byte[IdBytes];
        first.TryWriteBytes(a, bigEndian: true, out _);
        second.TryWriteBytes(b, bigEndian: true, out _);
        return a.SequenceCompareTo(b);
    }

    public static byte[] Seal(RekeyPayload payload, SyncKey oldKey)
    {
        byte[] plain = Serialize(payload);
        byte[] output = new byte[HeaderBytes + plain.Length + TagBytes];
        Magic.CopyTo(output, 0);
        Convert.FromHexString(oldKey.GroupId).CopyTo(output, MagicBytes);
        Span<byte> nonce = output.AsSpan(MagicBytes + GroupIdBytes, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(oldKey.Key, TagBytes);
        aes.Encrypt(nonce, plain, output.AsSpan(HeaderBytes, plain.Length), output.AsSpan(HeaderBytes + plain.Length, TagBytes), output.AsSpan(0, MagicBytes + GroupIdBytes));
        return output;
    }

    /// <returns>The payload, or null when it isn't a rekey file of this group or key, was changed, or doesn't parse.</returns>
    public static RekeyPayload? Open(ReadOnlySpan<byte> sealedBytes, SyncKey oldKey)
    {
        if (!IsRekey(sealedBytes) || !sealedBytes.Slice(MagicBytes, GroupIdBytes).SequenceEqual(Convert.FromHexString(oldKey.GroupId)))
        {
            return null;
        }

        byte[] plain = new byte[sealedBytes.Length - HeaderBytes - TagBytes];
        try
        {
            using var aes = new AesGcm(oldKey.Key, TagBytes);
            aes.Decrypt(
                sealedBytes.Slice(MagicBytes + GroupIdBytes, NonceBytes), sealedBytes[HeaderBytes..^TagBytes], sealedBytes[^TagBytes..], plain,
                sealedBytes[..(MagicBytes + GroupIdBytes)]);
        }
        catch (CryptographicException)
        {
            return null;
        }

        return Parse(plain);
    }

    /// <summary>The payload's bytes (the part inside the seal), exposed so two builds can be compared.</summary>
    public static byte[] Serialize(RekeyPayload payload)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(Version);
        WriteInt64(stream, payload.Epoch);
        WriteDevice(stream, payload.RemovedBy);
        WriteUInt16(stream, payload.Removed.Count);
        foreach (RekeyRemovedDevice removed in payload.Removed)
        {
            WriteDevice(stream, removed);
            stream.WriteByte(removed.Erase ? (byte)1 : (byte)0);
        }

        WriteUInt16(stream, payload.Wraps.Count);
        foreach (RekeyWrapRecord wrap in payload.Wraps)
        {
            WriteGuid(stream, wrap.DeviceId);
            stream.Write(wrap.EphemeralPublicKey);
            stream.Write(wrap.Nonce);
            stream.Write(wrap.Wrapped);
        }

        return stream.ToArray();
    }

    /// <returns>The payload, or null when the bytes aren't a complete version 1 payload.</returns>
    public static RekeyPayload? Parse(ReadOnlySpan<byte> plain)
    {
        var reader = new Reader(plain);
        if (!reader.TryReadByte(out byte version) || version != Version || !reader.TryReadInt64(out long epoch)
            || !TryReadDevice(ref reader, out RekeyRemovedDevice? removedBy) || !reader.TryReadUInt16(out int removedCount))
        {
            return null;
        }

        var removed = new List<RekeyRemovedDevice>();
        for (int index = 0; index < removedCount; index++)
        {
            if (!TryReadDevice(ref reader, out RekeyRemovedDevice? device) || !reader.TryReadByte(out byte erase) || erase > 1)
            {
                return null;
            }

            removed.Add(device with { Erase = erase == 1 });
        }

        if (!reader.TryReadUInt16(out int wrapCount))
        {
            return null;
        }

        var wraps = new List<RekeyWrapRecord>();
        for (int index = 0; index < wrapCount; index++)
        {
            if (!reader.TryReadGuid(out Guid id)
                || !reader.TryReadBytes(SyncDeviceKey.PublicKeyBytes, out byte[] ephemeral)
                || !reader.TryReadBytes(RekeyWrap.NonceLength, out byte[] nonce)
                || !reader.TryReadBytes(RekeyWrap.WrappedBytes, out byte[] wrapped))
            {
                return null;
            }

            wraps.Add(new RekeyWrapRecord(id, ephemeral, nonce, wrapped));
        }

        return reader.AtEnd ? new RekeyPayload(epoch, removedBy, removed, wraps) : null;
    }

    // A device's id, then its name (length and UTF-8).
    private static void WriteDevice(Stream stream, RekeyRemovedDevice device)
    {
        WriteGuid(stream, device.DeviceId);
        byte[] name = TruncatedName(device.Name);
        WriteUInt16(stream, name.Length);
        stream.Write(name);
    }

    private static bool TryReadDevice(ref Reader reader, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RekeyRemovedDevice? device)
    {
        device = null;
        if (!reader.TryReadGuid(out Guid id) || !reader.TryReadUInt16(out int nameLength) || nameLength > MaxNameBytes || !reader.TryReadBytes(nameLength, out byte[] name))
        {
            return false;
        }

        device = new RekeyRemovedDevice(id, Encoding.UTF8.GetString(name));
        return true;
    }

    // At most MaxNameBytes of UTF-8, cut at a character boundary.
    private static byte[] TruncatedName(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        if (bytes.Length <= MaxNameBytes)
        {
            return bytes;
        }

        int length = MaxNameBytes;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return bytes[..length];
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)value));
        stream.Write(bytes);
    }

    private static void WriteGuid(Stream stream, Guid id)
    {
        Span<byte> bytes = stackalloc byte[IdBytes];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        stream.Write(bytes);
    }

    // Reads the payload front to back; every read says whether enough bytes were left.
    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _position;

        public Reader(ReadOnlySpan<byte> bytes)
        {
            _bytes = bytes;
            _position = 0;
        }

        public readonly bool AtEnd => _position == _bytes.Length;

        public bool TryReadByte(out byte value)
        {
            value = 0;
            if (_bytes.Length - _position < 1)
            {
                return false;
            }

            value = _bytes[_position++];
            return true;
        }

        public bool TryReadUInt16(out int value)
        {
            value = 0;
            if (_bytes.Length - _position < 2)
            {
                return false;
            }

            value = BinaryPrimitives.ReadUInt16BigEndian(_bytes.Slice(_position, 2));
            _position += 2;
            return true;
        }

        public bool TryReadInt64(out long value)
        {
            value = 0;
            if (_bytes.Length - _position < 8)
            {
                return false;
            }

            value = BinaryPrimitives.ReadInt64BigEndian(_bytes.Slice(_position, 8));
            _position += 8;
            return true;
        }

        public bool TryReadGuid(out Guid value)
        {
            value = Guid.Empty;
            if (_bytes.Length - _position < IdBytes)
            {
                return false;
            }

            value = new Guid(_bytes.Slice(_position, IdBytes), bigEndian: true);
            _position += IdBytes;
            return true;
        }

        public bool TryReadBytes(int count, out byte[] value)
        {
            value = [];
            if (_bytes.Length - _position < count)
            {
                return false;
            }

            value = _bytes.Slice(_position, count).ToArray();
            _position += count;
            return true;
        }
    }
}
