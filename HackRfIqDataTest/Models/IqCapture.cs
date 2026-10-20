using System;
using System.Numerics;

namespace HackRfIqDataTest.Models;

/// <summary>
/// A contiguous block of IQ samples held in memory, either captured from the SDR or loaded from a recording.
/// </summary>
public sealed class IqCapture
{
    public required RadioTuning Tuning { get; init; }

    /// <summary>Time of the first sample.</summary>
    public required DateTime CapturedAtUtc { get; init; }

    /// <summary>
    /// Raw interleaved I/Q samples as signed 8-bit values (I0, Q0, I1, Q1, ...), exactly as the HackRF delivers them.
    /// </summary>
    public required byte[] Samples { get; init; }

    public double CenterFrequencyHz => this.Tuning.CenterFrequencyHz;

    public double SampleRateHz => this.Tuning.SampleRateHz;

    public int SampleCount => this.Samples.Length / 2;

    public SampleLevels GetLevels()
    {
        long sumOfSquares = 0;
        var peak = 0;
        var clipped = 0;
        foreach (var value in this.Samples)
        {
            var sample = (sbyte)value;
            sumOfSquares += sample * sample;
            peak = System.Math.Max(peak, System.Math.Abs((int)sample));
            if (sample is sbyte.MaxValue or sbyte.MinValue)
            {
                clipped++;
            }
        }

        return new SampleLevels(
            System.Math.Sqrt((double)sumOfSquares / this.Samples.Length), peak, 100.0 * clipped / this.Samples.Length);
    }

    /// <summary>
    /// Splits the interleaved bytes into individual I/Q samples without any scaling.
    /// </summary>
    public IqSample[] ToIqSamples()
    {
        var result = new IqSample[this.SampleCount];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new IqSample(i, (sbyte)this.Samples[2 * i], (sbyte)this.Samples[2 * i + 1]);
        }

        return result;
    }

    /// <summary>
    /// Converts the raw samples to complex values normalised to the range [-1, 1).
    /// </summary>
    public Complex[] ToComplex()
    {
        var result = new Complex[this.SampleCount];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Complex((sbyte)this.Samples[2 * i] / 128.0, (sbyte)this.Samples[2 * i + 1] / 128.0);
        }

        return result;
    }
}
