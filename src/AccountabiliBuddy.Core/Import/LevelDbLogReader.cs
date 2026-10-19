using System.Buffers.Binary;
using System.Text;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Reads the latest value of each key from a LevelDB write-ahead log (*.log), the format Chromium uses for an
/// extension's storage ("Local Extension Settings\&lt;id&gt;"). Only the log is read, not compacted *.ldb tables
/// (those are compressed); values that only live in a table are missing. Damaged data ends the read early.
/// </summary>
/// <remarks>
/// Log: 32 KiB blocks of records (4-byte checksum, 2-byte length, 1-byte type: full, first, middle, last).
/// A record's payload is a write batch: 8-byte sequence, 4-byte count, then per entry a tag (1 = put, 0 = delete),
/// a length-prefixed key and, for puts, a length-prefixed value.
/// </remarks>
public static class LevelDbLogReader
{
    private const int BlockSize = 32 * 1024;
    private const int HeaderSize = 7;
    private const byte FullRecord = 1;
    private const byte FirstRecord = 2;
    private const byte MiddleRecord = 3;
    private const byte LastRecord = 4;

    /// <summary>Key → latest value (UTF-8) from the logs in <paramref name="logFiles"/>, read oldest first.</summary>
    public static IReadOnlyDictionary<string, string> ReadLatestValues(IEnumerable<byte[]> logFiles)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (byte[] log in logFiles)
        {
            foreach (byte[] batch in Records(log))
            {
                ApplyBatch(batch, values);
            }
        }

        return values;
    }

    private static IEnumerable<byte[]> Records(byte[] log)
    {
        var pending = new List<byte>();
        int position = 0;
        while (position + HeaderSize <= log.Length)
        {
            int leftInBlock = BlockSize - position % BlockSize;
            if (leftInBlock < HeaderSize)
            {
                position += leftInBlock; // Block trailer padding.
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16LittleEndian(log.AsSpan(position + 4, 2));
            byte type = log[position + 6];
            int dataStart = position + HeaderSize;
            if (type == 0 && length == 0)
            {
                position += leftInBlock; // Zeroed space, e.g. preallocated.
                continue;
            }

            if (dataStart + length > log.Length || length > leftInBlock - HeaderSize)
            {
                yield break;
            }

            ReadOnlySpan<byte> data = log.AsSpan(dataStart, length);
            switch (type)
            {
                case FullRecord:
                    pending.Clear();
                    yield return data.ToArray();
                    break;
                case FirstRecord:
                    pending.Clear();
                    pending.AddRange(data.ToArray());
                    break;
                case MiddleRecord:
                    pending.AddRange(data.ToArray());
                    break;
                case LastRecord:
                    pending.AddRange(data.ToArray());
                    yield return pending.ToArray();
                    pending.Clear();
                    break;
                default:
                    yield break;
            }

            position = dataStart + length;
        }
    }

    private static void ApplyBatch(byte[] batch, Dictionary<string, string> values)
    {
        if (batch.Length < 12)
        {
            return;
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(batch.AsSpan(8, 4));
        int position = 12;
        for (uint i = 0; i < count && position < batch.Length; i++)
        {
            byte tag = batch[position++];
            if (!TryReadSlice(batch, ref position, out string? key))
            {
                return;
            }

            if (tag == 1)
            {
                if (!TryReadSlice(batch, ref position, out string? value))
                {
                    return;
                }

                values[key] = value;
            }
            else if (tag == 0)
            {
                values.Remove(key);
            }
            else
            {
                return;
            }
        }
    }

    private static bool TryReadSlice(byte[] data, ref int position, out string text)
    {
        text = string.Empty;
        if (!TryReadVarint(data, ref position, out int length) || length < 0 || position + length > data.Length)
        {
            return false;
        }

        text = Encoding.UTF8.GetString(data, position, length);
        position += length;
        return true;
    }

    private static bool TryReadVarint(byte[] data, ref int position, out int value)
    {
        value = 0;
        for (int shift = 0; shift <= 28 && position < data.Length; shift += 7)
        {
            byte current = data[position++];
            value |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
