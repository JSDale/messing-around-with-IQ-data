using System;
using System.Buffers;
using System.Threading;

namespace HackRfIqDataTest;

/// <summary>
/// A block of raw interleaved signed 8-bit I/Q bytes from the radio. Dispose it when finished so its buffer goes back
/// to the pool.
/// </summary>
public sealed class IqBlock : IDisposable
{
    private byte[]? _buffer;

    private IqBlock(byte[] buffer, int length, long sampleIndex)
    {
        this._buffer = buffer;
        this.Length = length;
        this.SampleIndex = sampleIndex;
    }

    /// <summary>
    /// Position of this block's first sample in the stream, counting any samples dropped before it. If it is further
    /// on than the end of the previous block, samples were lost in between.
    /// </summary>
    public long SampleIndex { get; }

    public int Length { get; }

    public int SampleCount => this.Length / 2;

    public ReadOnlyMemory<byte> Data
    {
        get
        {
            ObjectDisposedException.ThrowIf(this._buffer is null, this);
            return this._buffer.AsMemory(0, this.Length);
        }
    }

    public static IqBlock Copy(ReadOnlySpan<byte> data, long sampleIndex)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buffer);
        return new IqBlock(buffer, data.Length, sampleIndex);
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref this._buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
