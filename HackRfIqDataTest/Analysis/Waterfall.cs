using System;
using System.Numerics;
using System.Threading;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest.Analysis;

/// <param name="PowerDb">Row-major power values in dBFS: one row per time step, one column per frequency bin.</param>
/// <param name="Width">Number of frequency bins (the FFT size). Column 0 is the lowest frequency.</param>
/// <param name="Height">Number of time rows. Row 0 is the start of the capture.</param>
/// <param name="FramesPerRow">How many FFTs were averaged into each row.</param>
/// <param name="FloorDb">Bottom of the suggested colour range (5th percentile, roughly the noise floor).</param>
/// <param name="PeakDb">Top of the suggested colour range (strongest bin).</param>
public sealed record WaterfallData(float[] PowerDb, int Width, int Height, int FramesPerRow, double FloorDb, double PeakDb);

public static class Waterfall
{
    /// <summary>
    /// Splits the capture into consecutive blocks of <paramref name="fftSize"/> samples and computes the power
    /// spectrum of each. If that gives more than <paramref name="maxRows"/> spectra, neighbouring spectra are averaged
    /// together, which also smooths out noise.
    /// </summary>
    public static WaterfallData Compute(IqCapture capture, int fftSize, int maxRows, CancellationToken cancellationToken)
    {
        var samples = capture.ToComplex();
        var frameCount = samples.Length / fftSize;
        if (frameCount == 0)
        {
            throw new ArgumentException($"Capture has fewer samples than the FFT size ({fftSize}).");
        }

        var framesPerRow = (int)Math.Ceiling(frameCount / (double)maxRows);
        var rows = frameCount / framesPerRow;

        // A Hann window tapers each block to zero at its edges. Without it the FFT treats the block as if it repeats
        // forever, the jump at the join smears energy across all bins ("spectral leakage").
        var window = new double[fftSize];
        var windowSum = 0.0;
        for (var i = 0; i < fftSize; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
            windowSum += window[i];
        }

        // Dividing by (sum of window)^2 means a full-scale sine wave reads 0 dBFS regardless of FFT size or window.
        var normalisation = windowSum * windowSum * framesPerRow;

        var powerDb = new float[rows * fftSize];
        var frame = new Complex[fftSize];
        var accumulated = new double[fftSize];
        for (var row = 0; row < rows; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(accumulated);

            for (var f = 0; f < framesPerRow; f++)
            {
                var offset = (row * framesPerRow + f) * fftSize;
                for (var i = 0; i < fftSize; i++)
                {
                    frame[i] = samples[offset + i] * window[i];
                }

                Fft.Forward(frame);

                // The FFT puts 0 Hz (the centre frequency) in bin 0 and negative frequencies in the upper half.
                // Rotating by half the length ("fftshift") orders the bins from lowest to highest frequency.
                for (var bin = 0; bin < fftSize; bin++)
                {
                    var magnitude = frame[bin].Magnitude;
                    accumulated[(bin + fftSize / 2) % fftSize] += magnitude * magnitude;
                }
            }

            for (var column = 0; column < fftSize; column++)
            {
                powerDb[row * fftSize + column] = (float)(10 * Math.Log10(accumulated[column] / normalisation + 1e-20));
            }
        }

        var sorted = (float[])powerDb.Clone();
        Array.Sort(sorted);
        return new WaterfallData(powerDb, fftSize, rows, framesPerRow, sorted[sorted.Length / 20], sorted[^1]);
    }
}
