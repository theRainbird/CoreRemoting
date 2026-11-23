using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels.Common;
using Xunit;

namespace CoreRemoting.Tests.Channels;

/// <summary>
/// Tests for the shared length prefixed wire framing helper.
/// </summary>
public class LengthPrefixedFrameTests
{
    [Fact]
    public async Task Write_then_read_roundtrips_message()
    {
        using var ms = new MemoryStream();
        var message = new byte[] { 1, 2, 3, 4, 5 };

        await LengthPrefixedFrame.WriteFrameAsync(ms, message);

        ms.Position = 0;
        var read = await LengthPrefixedFrame.ReadFrameAsync(ms);

        Assert.Equal(message, read);
    }

    [Fact]
    public async Task Write_then_read_roundtrips_empty_message()
    {
        using var ms = new MemoryStream();

        await LengthPrefixedFrame.WriteFrameAsync(ms, Array.Empty<byte>());

        ms.Position = 0;
        var read = await LengthPrefixedFrame.ReadFrameAsync(ms);

        Assert.NotNull(read);
        Assert.Empty(read);
    }

    [Fact]
    public async Task Write_then_read_roundtrips_large_message_across_chunks()
    {
        using var ms = new MemoryStream();
        var message = new byte[LengthPrefixedFrame.DefaultChunkSize * 2 + 1234];
        new Random(42).NextBytes(message);

        await LengthPrefixedFrame.WriteFrameAsync(ms, message);

        ms.Position = 0;
        var read = await LengthPrefixedFrame.ReadFrameAsync(ms);

        Assert.Equal(message, read);
    }

    [Fact]
    public async Task Multiple_frames_read_back_in_order()
    {
        using var ms = new MemoryStream();
        var first = new byte[] { 9, 8, 7 };
        var second = Array.Empty<byte>();
        var third = new byte[] { 5 };

        await LengthPrefixedFrame.WriteFrameAsync(ms, first);
        await LengthPrefixedFrame.WriteFrameAsync(ms, second);
        await LengthPrefixedFrame.WriteFrameAsync(ms, third);

        ms.Position = 0;
        Assert.Equal(first, await LengthPrefixedFrame.ReadFrameAsync(ms));
        Assert.Equal(second, await LengthPrefixedFrame.ReadFrameAsync(ms));
        Assert.Equal(third, await LengthPrefixedFrame.ReadFrameAsync(ms));
        Assert.Null(await LengthPrefixedFrame.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task Header_is_little_endian_message_length()
    {
        using var ms = new MemoryStream();
        var message = new byte[0x01020304];

        await LengthPrefixedFrame.WriteFrameAsync(ms, message);

        var bytes = ms.ToArray();
        Assert.Equal(0x04, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0x02, bytes[2]);
        Assert.Equal(0x01, bytes[3]);
    }

    [Fact]
    public async Task Read_returns_null_on_empty_stream()
    {
        using var ms = new MemoryStream();

        Assert.Null(await LengthPrefixedFrame.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task Read_returns_null_on_partial_header()
    {
        using var ms = new MemoryStream(new byte[] { 1, 2 });

        Assert.Null(await LengthPrefixedFrame.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task Read_returns_null_on_partial_body()
    {
        using var ms = new MemoryStream();
        await LengthPrefixedFrame.WriteFrameAsync(ms, new byte[100]);

        // cut off the body: keep header + 50 body bytes
        var truncated = new byte[LengthPrefixedFrame.HeaderSize + 50];
        Array.Copy(ms.ToArray(), truncated, truncated.Length);

        using var truncatedStream = new MemoryStream(truncated);
        Assert.Null(await LengthPrefixedFrame.ReadFrameAsync(truncatedStream));
    }

    [Fact]
    public async Task Read_throws_on_negative_length()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LengthPrefixedFrame.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task Read_throws_on_oversized_length()
    {
        using var ms = new MemoryStream();
        await ms.WriteAsync(BitConverter.GetBytes(LengthPrefixedFrame.DefaultMaxMessageSize + 1));
        ms.Position = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => LengthPrefixedFrame.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task Read_handles_one_byte_at_a_time_stream()
    {
        using var ms = new MemoryStream();
        var message = new byte[] { 42, 43, 44, 45, 46, 47 };
        await LengthPrefixedFrame.WriteFrameAsync(ms, message);

        using var dribble = new DribbleStream(ms.ToArray());
        var read = await LengthPrefixedFrame.ReadFrameAsync(dribble);

        Assert.Equal(message, read);
    }

    /// <summary>
    /// Stream that returns at most one byte per read call,
    /// to simulate partial reads on slow transports.
    /// </summary>
    private sealed class DribbleStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public DribbleStream(byte[] data) => _data = data;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length || count == 0)
                return 0;

            buffer[offset] = _data[_position++];
            return 1;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
