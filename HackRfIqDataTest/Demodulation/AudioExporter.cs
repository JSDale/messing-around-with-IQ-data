using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;
using HackRfIqDataTest.Storage;

namespace HackRfIqDataTest.Demodulation;

public static class AudioExporter
{
    // Recordings can be far bigger than memory, so they are demodulated a chunk at a time.
    private const int ChunkSamples = 1 << 20;

    /// <summary>
    /// Demodulates the whole recording to a WAV file. Gaps from dropped samples are not filled, so audio after a gap
    /// arrives early.
    /// </summary>
    public static async Task ExportAsync(
        RecordingSummary recording, IDemodulator demodulator, string wavPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var wav = new WavWriter(wavPath, demodulator.AudioSampleRate);
            for (long start = 0; start < recording.SampleCount; start += ChunkSamples)
            {
                var chunk = await RecordingLibrary.LoadAsync(recording, start, ChunkSamples, cancellationToken);
                wav.Write(demodulator.Process(chunk.ToComplex()));
                progress?.Report((double)(start + chunk.SampleCount) / recording.SampleCount);
            }
        }
        catch
        {
            File.Delete(wavPath);
            throw;
        }
    }
}
