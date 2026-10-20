using System.Buffers.Binary;
using System.Text.Json;

namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// How messages travel on the link (ADR 14): a 4-byte big-endian length, then that many bytes of UTF-8 JSON holding
/// one <see cref="LinkEnvelope"/> (camelCase names). Frames over <see cref="MaxFrameBytes"/> are refused.
/// </summary>
public static class LinkFraming
{
    public const int MaxFrameBytes = 1024 * 1024;

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Writes one message. Callers serialise their writes (one at a time per stream).</summary>
    public static async Task WriteAsync(Stream stream, LinkEnvelope envelope, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (body.Length > MaxFrameBytes)
        {
            throw new InvalidDataException("The message is too large for the link.");
        }

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The next message, or null when the other side closed the connection.</returns>
    /// <exception cref="InvalidDataException">The bytes aren't a message of this protocol.</exception>
    public static async Task<LinkEnvelope?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaxFrameBytes)
        {
            throw new InvalidDataException("The message length isn't valid.");
        }

        byte[] body = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, body, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LinkEnvelope>(body, JsonOptions) is { Type.Length: > 0 } envelope
                ? envelope
                : throw new InvalidDataException("The message has no type.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The message isn't valid JSON.", ex);
        }
    }

    /// <returns>False when the stream ended first (a closed connection, or one that stopped mid-message).</returns>
    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return true;
    }
}
