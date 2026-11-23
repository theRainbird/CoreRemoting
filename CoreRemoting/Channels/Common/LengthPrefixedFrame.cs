using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoreRemoting.Channels.Common;

/// <summary>
/// Helper for the length prefixed wire framing used by the CoreRemoting channels:
/// a 4 byte little-endian message length followed by the message body.
/// </summary>
public static class LengthPrefixedFrame
{
    /// <summary>
    /// Size of the length header in bytes.
    /// </summary>
    public const int HeaderSize = 4;

    /// <summary>
    /// Default sanity cap for the message size to avoid accidental OOM (1 GiB).
    /// </summary>
    public const int DefaultMaxMessageSize = 1_073_741_824;

    /// <summary>
    /// Default chunk size to write message bodies (64 KiB),
    /// to bypass OS pipe write size limits.
    /// </summary>
    public const int DefaultChunkSize = 64 * 1024;

    /// <summary>
    /// Reads one length prefixed frame from the given stream.
    /// </summary>
    /// <param name="stream">Stream to read from</param>
    /// <param name="maxMessageSize">Maximum allowed message size</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>
    /// The message body, or <c>null</c> if the stream ended before the next
    /// frame was complete (clean end of stream).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the frame header declares an invalid or oversized message length.
    /// </exception>
    public static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        int maxMessageSize = DefaultMaxMessageSize,
        CancellationToken ct = default)
    {
        // read the length header exactly
        // (manual loop: ReadExactlyAsync is not available on netstandard2.0)
        var headerBuffer = new byte[HeaderSize];
        var headerBytesRead = 0;

        while (headerBytesRead < HeaderSize)
        {
            var readNow = await stream
                .ReadAsync(headerBuffer, headerBytesRead, HeaderSize - headerBytesRead, ct)
                .ConfigureAwait(false);

            if (readNow == 0)
                return null; // end of stream

            headerBytesRead += readNow;
        }

        var messageLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);

        if (messageLength < 0 || messageLength > maxMessageSize)
            throw new InvalidOperationException($"Invalid message length: {messageLength}");

        // read the message body
        var messageBuffer = new byte[messageLength];
        var totalBytesRead = 0;

        while (totalBytesRead < messageLength)
        {
            var read = await stream
                .ReadAsync(messageBuffer, totalBytesRead, messageLength - totalBytesRead, ct)
                .ConfigureAwait(false);

            if (read == 0)
                return null; // end of stream mid frame

            totalBytesRead += read;
        }

        return messageBuffer;
    }

    /// <summary>
    /// Writes one length prefixed frame to the given stream.
    /// </summary>
    /// <param name="stream">Stream to write to</param>
    /// <param name="message">Message body</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="chunkSize">Chunk size to write the message body</param>
    public static async Task WriteFrameAsync(
        Stream stream,
        byte[] message,
        CancellationToken ct = default,
        int chunkSize = DefaultChunkSize)
    {
        message ??= Array.Empty<byte>();

        var headerBuffer = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer, message.Length);

        await stream
            .WriteAsync(headerBuffer, 0, HeaderSize, ct)
            .ConfigureAwait(false);

        var offset = 0;

        while (offset < message.Length)
        {
            var toWrite = Math.Min(chunkSize, message.Length - offset);
            await stream
                .WriteAsync(message, offset, toWrite, ct)
                .ConfigureAwait(false);
            offset += toWrite;
        }

        await stream
            .FlushAsync(ct)
            .ConfigureAwait(false);
    }
}
