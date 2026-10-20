using System;
using System.Numerics;

namespace HackRfIqDataTest.Demodulation;

public interface IDemodulator
{
    int AudioSampleRate { get; }

    /// <summary>
    /// Demodulates the next block of IQ samples, continuing from the previous block. Returns audio in about ±1.
    /// </summary>
    float[] Process(ReadOnlySpan<Complex> iq);
}
