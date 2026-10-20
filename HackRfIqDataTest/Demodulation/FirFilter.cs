using System;
using System.Numerics;

namespace HackRfIqDataTest.Demodulation;

public static class FirFilter
{
    /// <summary>
    /// Designs a windowed-sinc low-pass filter. The ideal low-pass impulse response is a sinc; truncating it to a
    /// finite length causes ripple, so it is tapered with a Hann window. Taps are normalised for 0 dB gain at 0 Hz.
    /// </summary>
    /// <param name="transitionHz">Roughly how wide the slope from pass to stop is. Narrower needs more taps.</param>
    public static double[] LowPass(double cutoffHz, double transitionHz, double sampleRateHz)
    {
        // A Hann-windowed filter's transition band is about 4 / N of the sample rate.
        var tapCount = (int)(4 * sampleRateHz / transitionHz) | 1;
        var taps = new double[tapCount];
        var middle = (tapCount - 1) / 2.0;
        var normalisedCutoff = cutoffHz / sampleRateHz;
        var sum = 0.0;
        for (var i = 0; i < tapCount; i++)
        {
            var t = i - middle;
            var sinc = t == 0 ? 2 * normalisedCutoff : Math.Sin(2 * Math.PI * normalisedCutoff * t) / (Math.PI * t);
            var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (tapCount - 1)));
            taps[i] = sinc * window;
            sum += taps[i];
        }

        for (var i = 0; i < tapCount; i++)
        {
            taps[i] /= sum;
        }

        return taps;
    }
}

/// <summary>
/// A FIR filter that also decimates (keeps every Nth output). Only the outputs that are kept are calculated.
/// It remembers the end of each block, so a long signal can be filtered in chunks without clicks at the joins.
/// </summary>
public sealed class DecimatingFir<T> where T : INumberBase<T>
{
    private readonly T[] _taps;
    private readonly int _decimation;
    private T[] _history;
    // How far past the start of the next block the next kept output is.
    private int _phase;

    public DecimatingFir(double[] taps, int decimation)
    {
        this._taps = Array.ConvertAll(taps, T.CreateChecked);
        this._decimation = decimation;
        this._history = new T[taps.Length - 1];
        Array.Fill(this._history, T.Zero);
    }

    public T[] Process(ReadOnlySpan<T> input)
    {
        // Join the end of the previous block to this one so the filter sees one continuous signal.
        var buffer = new T[this._history.Length + input.Length];
        this._history.CopyTo(buffer, 0);
        input.CopyTo(buffer.AsSpan(this._history.Length));

        var first = this._history.Length + this._phase;
        var output = new T[Math.Max(0, (buffer.Length - first + this._decimation - 1) / this._decimation)];
        var index = first;
        for (var o = 0; o < output.Length; o++, index += this._decimation)
        {
            // Convolution: each output is the sum of the recent inputs weighted by the taps.
            var sum = T.Zero;
            for (var k = 0; k < this._taps.Length; k++)
            {
                sum += this._taps[k] * buffer[index - k];
            }

            output[o] = sum;
        }

        this._phase = index - buffer.Length;
        this._history = buffer[^this._history.Length..];
        return output;
    }
}
