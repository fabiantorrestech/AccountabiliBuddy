using System.Buffers.Binary;

namespace AccountabiliBuddy.Core.Browser;

/// <summary>
/// Browser native messaging framing: a 4-byte little-endian length followed by that many bytes of UTF-8 JSON.
/// Used on both legs (browser ↔ native host over stdio, native host ↔ app over the named pipe), so the host
/// can relay messages without parsing them.
/// This file is also compiled into the native host, which deliberately does not reference Core.
/// </summary>
public static class NativeMessageFraming
{
    /// <summary>Larger messages are rejected. Chrome itself caps messages from the host at 1 MB.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    /// <returns>The message, or null when the stream ended cleanly between messages.</returns>
    /// <exception cref="InvalidDataException">The length is invalid or the stream ended mid-message.</exception>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[4];
        int headerRead = await ReadFullyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerRead == 0)
        {
            return null;
        }

        if (headerRead < header.Length)
        {
            throw new InvalidDataException("The stream ended inside a message header.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxMessageBytes)
        {
            throw new InvalidDataException($"Message length {length} is outside the allowed range.");
        }

        byte[] body = new byte[length];
        if (await ReadFullyAsync(stream, body, cancellationToken).ConfigureAwait(false) < length)
        {
            throw new InvalidDataException("The stream ended inside a message.");
        }

        return body;
    }

    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (message.Length > MaxMessageBytes)
        {
            throw new ArgumentException($"Message is larger than {MaxMessageBytes} bytes.", nameof(message));
        }

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
