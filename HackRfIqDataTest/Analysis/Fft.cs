using System;
using System.Numerics;

namespace HackRfIqDataTest.Analysis;

/// <summary>
/// Iterative radix-2 Cooley-Tukey FFT. Written out rather than taken from a library so the steps are visible.
/// </summary>
public static class Fft
{
    /// <summary>
    /// Transforms <paramref name="data"/> in place from the time domain to the frequency domain.
    /// The length must be a power of two. On return, bin k holds frequency k * sampleRate / N for k &lt; N/2,
    /// and the upper half holds the negative frequencies (k - N) * sampleRate / N.
    /// </summary>
    public static void Forward(Complex[] data)
    {
        var n = data.Length;
        if (n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("FFT length must be a power of two.", nameof(data));
        }

        // Reorder the input into bit-reversed index order so the butterflies below can work in place.
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        // Combine pairs of DFTs of length len/2 into DFTs of length len, doubling each pass.
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var rotation = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < n; start += len)
            {
                var twiddle = Complex.One;
                for (var k = 0; k < len / 2; k++)
                {
                    var even = data[start + k];
                    var odd = data[start + k + len / 2] * twiddle;
                    data[start + k] = even + odd;
                    data[start + k + len / 2] = even - odd;
                    twiddle *= rotation;
                }
            }
        }
    }
}
