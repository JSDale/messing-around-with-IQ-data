using System;
using System.Threading.Channels;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest;

/// <summary>
/// A running stream of samples from the radio. Disposing it stops the radio.
/// </summary>
public interface IIqStream : IAsyncDisposable
{
    RadioTuning Tuning { get; }

    DateTime StartedAtUtc { get; }

    /// <summary>
    /// Blocks in the order they were received. If the reader falls too far behind, new blocks are dropped rather than
    /// stalling the radio; <see cref="IqBlock.SampleIndex"/> shows where.
    /// </summary>
    ChannelReader<IqBlock> Blocks { get; }

    long DroppedSamples { get; }
}
