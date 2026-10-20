using System;
using System.Numerics;

namespace HackRfIqDataTest.Demodulation;

/// <summary>
/// Mono broadcast FM. See learning-journal.md for the reasoning behind each step.
/// </summary>
public sealed class FmBroadcastDemodulator : IDemodulator
{
    // A station is about 200 kHz wide.
    private const double ChannelCutoffHz = 100e3;
    private const double MinChannelRateHz = 240e3;
    private const double MaxDeviationHz = 75e3;
    private const double AudioCutoffHz = 15e3;
    private const double MinAudioRateHz = 48e3;
    // UK and Europe use 50 µs; the Americas use 75 µs.
    private const double DeemphasisSeconds = 50e-6;

    private readonly Complex _shiftStep;
    private readonly DecimatingFir<Complex> _channelFilter;
    private readonly DecimatingFir<double> _audioFilter;
    private readonly double _discriminatorScale;
    private readonly double _deemphasisAlpha;

    private Complex _oscillator = Complex.One;
    private Complex _previous;
    private double _deemphasisState;

    /// <param name="offsetHz">Station frequency minus the recording's centre frequency.</param>
    public FmBroadcastDemodulator(double sampleRateHz, double offsetHz)
    {
        if (Math.Abs(offsetHz) + ChannelCutoffHz > sampleRateHz / 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offsetHz),
                $"The station must be within ±{(sampleRateHz / 2 - ChannelCutoffHz) / 1e3:F0} kHz of the centre of this recording.");
        }

        // Decimate in two stages: first to the ~250 kS/s needed to hold one station, then to an audio rate.
        var channelDecimation = Math.Max(1, (int)(sampleRateHz / MinChannelRateHz));
        var channelRateHz = sampleRateHz / channelDecimation;
        var audioDecimation = Math.Max(1, (int)(channelRateHz / MinAudioRateHz));
        this.AudioSampleRate = (int)Math.Round(channelRateHz / audioDecimation);

        this._shiftStep = Complex.FromPolarCoordinates(1, -2 * Math.PI * offsetHz / sampleRateHz);
        this._channelFilter = new DecimatingFir<Complex>(FirFilter.LowPass(ChannelCutoffHz, 50e3, sampleRateHz), channelDecimation);
        this._audioFilter = new DecimatingFir<double>(FirFilter.LowPass(AudioCutoffHz, 4e3, channelRateHz), audioDecimation);
        this._discriminatorScale = channelRateHz / (2 * Math.PI) / MaxDeviationHz;
        this._deemphasisAlpha = 1 - Math.Exp(-1 / (channelRateHz * DeemphasisSeconds));
    }

    public int AudioSampleRate { get; }

    public float[] Process(ReadOnlySpan<Complex> iq)
    {
        // 1. Shift the station to 0 Hz by multiplying with a complex sine rotating the opposite way.
        var shifted = new Complex[iq.Length];
        for (var i = 0; i < iq.Length; i++)
        {
            shifted[i] = iq[i] * this._oscillator;
            this._oscillator *= this._shiftStep;
        }

        // Repeated multiplication slowly drifts the oscillator's amplitude away from 1 through rounding.
        this._oscillator /= this._oscillator.Magnitude;

        // 2. Keep only this station, and drop to ~250 kS/s.
        var channel = this._channelFilter.Process(shifted);

        // 3. FM discriminator: the phase change between samples is the instantaneous frequency.
        // 4. De-emphasis: a single-pole low-pass that undoes the transmitter's treble boost.
        var deemphasised = new double[channel.Length];
        for (var i = 0; i < channel.Length; i++)
        {
            var phaseChange = (channel[i] * Complex.Conjugate(this._previous)).Phase;
            this._previous = channel[i];

            this._deemphasisState += this._deemphasisAlpha * (phaseChange * this._discriminatorScale - this._deemphasisState);
            deemphasised[i] = this._deemphasisState;
        }

        // 5. Keep mono audio (below 15 kHz), dropping the stereo pilot, stereo signal and RDS, and drop to ~50 kS/s.
        return Array.ConvertAll(this._audioFilter.Process(deemphasised), sample => (float)sample);
    }
}
