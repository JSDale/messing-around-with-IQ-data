using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest.HackRf;

/// <summary>
/// Receives IQ samples from a HackRF One. The radio is centred between the start and end frequencies, with the
/// sample rate set to cover the span (2 MHz minimum, 20 MHz maximum).
/// </summary>
public sealed class HackRfRadio : ISoftwareDefineRadio
{
    private const double MinFrequencyHz = 1e6;
    private const double MaxFrequencyHz = 6e9;
    private const double MinSampleRateHz = 2e6;
    private const double MaxSampleRateHz = 20e6;
    // Each sample is shown as a table row, so keep captures to a size the UI can hold in memory.
    private const int MaxSamplesPerCapture = 10_000_000;
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    private double _startFrequencyHz;
    private double _endFrequencyHz;
    private uint _lnaGainDb = 16;
    private uint _vgaGainDb = 20;
    private bool _ampEnabled;

    public void SetStartFrequency(double startFrequencyHz)
    {
        this._startFrequencyHz = startFrequencyHz;
    }

    public void SetEndFrequency(double endFrequencyHz)
    {
        this._endFrequencyHz = endFrequencyHz;
    }

    public void SetLnaGain(uint lnaGainDb)
    {
        this._lnaGainDb = lnaGainDb;
    }

    public void SetVgaGain(uint vgaGainDb)
    {
        this._vgaGainDb = vgaGainDb;
    }

    public void SetAmpEnabled(bool ampEnabled)
    {
        this._ampEnabled = ampEnabled;
    }

    public Task<IIqStream> StartStreaming(CancellationToken cancellationToken = default)
    {
        var tuning = this.GetTuning();
        return Task.Run<IIqStream>(() => HackRfStream.Start(tuning), cancellationToken);
    }

    public async Task<IqCapture> StartCapture(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var tuning = this.GetTuning();

        var sampleCount = (long)Math.Ceiling(duration.TotalSeconds * tuning.SampleRateHz);
        if (sampleCount > MaxSamplesPerCapture)
        {
            throw new ArgumentException(
                $"Duration too long: at {tuning.SampleRateHz / 1e6:F1} MS/s the maximum is {MaxSamplesPerCapture / tuning.SampleRateHz * 1000:F0} ms.");
        }

        var samples = new byte[sampleCount * 2];
        var written = 0;

        await using var stream = await this.StartStreaming(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration + CaptureTimeout);
        try
        {
            while (written < samples.Length)
            {
                using var block = await stream.Blocks.ReadAsync(timeout.Token);

                // A capture must be contiguous, so a gap from dropped samples makes it unusable.
                if (block.SampleIndex != written / 2)
                {
                    throw new HackRfException("Samples were dropped during the capture.");
                }

                var toCopy = Math.Min(block.Length, samples.Length - written);
                block.Data.Span[..toCopy].CopyTo(samples.AsSpan(written));
                written += toCopy;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No complete capture received from the HackRF within {(duration + CaptureTimeout).TotalSeconds:F0} s.");
        }
        catch (ChannelClosedException)
        {
            throw new HackRfException("The HackRF stopped streaming before the capture completed.");
        }

        return new IqCapture { Tuning = tuning, CapturedAtUtc = stream.StartedAtUtc, Samples = samples };
    }

    private RadioTuning GetTuning()
    {
        if (this._startFrequencyHz < MinFrequencyHz || this._endFrequencyHz > MaxFrequencyHz)
        {
            throw new ArgumentOutOfRangeException(null, "Frequencies must be between 1 MHz and 6 GHz.");
        }

        var spanHz = this._endFrequencyHz - this._startFrequencyHz;
        if (spanHz <= 0)
        {
            throw new ArgumentException("End frequency must be greater than start frequency.");
        }

        if (spanHz > MaxSampleRateHz)
        {
            throw new ArgumentException($"Span must be {MaxSampleRateHz / 1e6} MHz or less.");
        }

        return new RadioTuning(
            (this._startFrequencyHz + this._endFrequencyHz) / 2,
            Math.Max(spanHz, MinSampleRateHz),
            this._lnaGainDb,
            this._vgaGainDb,
            this._ampEnabled);
    }
}
