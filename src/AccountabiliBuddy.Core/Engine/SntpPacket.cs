using System.Buffers.Binary;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The 48-byte SNTP packet (RFC 4330) used to read network time (ADR 70): a client request carrying nothing but its version and
/// mode, and the reading of a server's answer into the offset between this device's clock and the server's.
/// </summary>
public static class SntpPacket
{
    public const int Length = 48;

    // Seconds from 1900-01-01 (NTP's era 0) to 1970-01-01.
    private const long SecondsFrom1900To1970 = 2_208_988_800L;

    /// <summary>A client request: leap indicator 0, version 4, mode 3 (client), everything else zero.</summary>
    public static byte[] Request()
    {
        var packet = new byte[Length];
        packet[0] = 0b00_100_011;
        return packet;
    }

    /// <summary>
    /// How far the server's clock is ahead of the clock that measured <paramref name="sentAt"/> and <paramref name="receivedAt"/>
    /// (add it to that clock's time to get network time). Null for an answer that can't be trusted: too short, not from a server,
    /// unsynchronised (leap indicator 3 or stratum 0), or with no transmit time.
    /// </summary>
    public static TimeSpan? Offset(ReadOnlySpan<byte> response, DateTimeOffset sentAt, DateTimeOffset receivedAt)
    {
        if (response.Length < Length)
        {
            return null;
        }

        int leap = response[0] >> 6;
        int mode = response[0] & 0b111;
        int stratum = response[1];
        if (leap == 3 || mode != 4 || stratum is 0 or > 15)
        {
            return null;
        }

        DateTimeOffset? serverReceived = Read(response[32..40]);
        DateTimeOffset? serverSent = Read(response[40..48]);
        if (serverReceived is not DateTimeOffset t2 || serverSent is not DateTimeOffset t3)
        {
            return null;
        }

        // ((t2 - t1) + (t3 - t4)) / 2: the round trip's two halves cancel out.
        return ((t2 - sentAt) + (t3 - receivedAt)) / 2;
    }

    // An NTP timestamp: 32 bits of seconds since 1900, 32 bits of fraction. Zero means "not set".
    private static DateTimeOffset? Read(ReadOnlySpan<byte> timestamp)
    {
        ulong seconds = BinaryPrimitives.ReadUInt32BigEndian(timestamp[..4]);
        ulong fraction = BinaryPrimitives.ReadUInt32BigEndian(timestamp[4..8]);
        if (seconds == 0 && fraction == 0)
        {
            return null;
        }

        long unixMilliseconds = ((long)seconds - SecondsFrom1900To1970) * 1000 + (long)(fraction * 1000 / 0x1_0000_0000UL);
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
    }
}
