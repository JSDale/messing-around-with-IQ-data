# Learning Journal

## 2026-10-20: Demodulating broadcast FM to a WAV file

**Goal:** turn a capture of a local FM station at 107.2 MHz (visible on the waterfall) into a sound file.

All the steps work on the `Complex[]` from `IqCapture.ToComplex()`. The numbers below assume a capture at 2 MS/s (`fs = 2_000_000`).

### The signal chain at a glance

| Step | What it does | Sample rate after |
|---|---|---|
| 0. Capture off-centre | Keep the station away from the HackRF's DC spike | 2 MS/s |
| 1. Frequency shift | Move the station to 0 Hz | 2 MS/s |
| 2. Low-pass + decimate ÷8 | Keep only the ~200 kHz wide station | 250 kS/s |
| 3. FM discriminator | Phase change per sample → audio (actually the "MPX" signal) | 250 kS/s |
| 4. De-emphasis | Undo the treble boost applied by the transmitter | 250 kS/s |
| 5. Low-pass 15 kHz + decimate ÷5 | Keep mono audio only | 50 kS/s |
| 6. Write WAV | 16-bit PCM file | 50 kS/s |

### 0. Capture off-centre

The HackRF has a DC offset: a spike at exactly the centre frequency (the bright line in the middle of the waterfall). If you tune straight to 107.2 MHz, the station sits on that spike and the audio is distorted.

Instead, tune off to one side. For example, enter **106.5 – 108.5 MHz**: the radio centres on 107.5 MHz, so the station is **300 kHz below centre**.

### 1. Frequency shift ("tuning" in software)

Multiplying a signal by the complex sine e^(jωn) shifts every frequency in it by ω. To move the station from -300 kHz to 0 Hz, multiply by a complex sine at minus the offset:

```csharp
var offsetHz = 107.2e6 - capture.CenterFrequencyHz;   // -300 kHz
for (var n = 0; n < x.Length; n++)
{
    x[n] *= Complex.FromPolarCoordinates(1, -2 * Math.PI * offsetHz * n / fs);
}
```

**Why it works:** each sample is a point rotating around the origin. Multiplying by another rotating point adds the two rotation speeds together. A negative rotation subtracts the offset.

### 2. Low-pass filter, then decimate to 250 kS/s

A broadcast FM station is about 200 kHz wide, but the capture is 2 MHz wide and full of other stations and noise.

1. **Low-pass filter** with a cutoff around 100 kHz, which keeps ±100 kHz around 0 Hz.
2. **Decimate by 8**: keep every 8th sample, so 2 MS/s becomes 250 kS/s.

**Filter before decimating.** After decimating, the new Nyquist limit (half the sample rate) is 125 kHz. Anything above it would fold back on top of the station: this is *aliasing*.

A **windowed-sinc FIR** is the standard first filter to write:
- The ideal low-pass impulse response is a sinc function, `sin(2π·fc·t) / (π·t)`.
- Truncate it to N taps and multiply by a window, for example the same Hann window used in the waterfall. Without the window, the sharp truncation causes ripple in the filter's response.
- Normalise the taps so they sum to 1, which gives 0 dB gain at 0 Hz.
- Filter by convolution: each output sample is the sum of `taps[k] * input[n - k]`.
- More taps give a sharper cutoff but cost more computation.

### 3. FM demodulation (the discriminator)

FM stores the audio in the **instantaneous frequency**: how fast the IQ point is rotating. Frequency is rate of change of phase, so measure the phase change between consecutive samples:

```csharp
for (var n = 1; n < y.Length; n++)
{
    mpx[n] = (y[n] * Complex.Conjugate(y[n - 1])).Phase;   // radians per sample
}
```

- Multiplying by the conjugate subtracts the previous sample's phase, leaving only the phase difference. `.Phase` returns it in the range -π to π.
- Multiply by `fs / (2π)` to convert radians per sample into Hz.
- Broadcast FM deviates up to **±75 kHz**, so divide the result in Hz by 75 kHz to bring it to roughly ±1.

**Why IQ matters here:** with real-only samples (no Q) you can't tell which way the signal is rotating, so you can't recover the phase this simply. IQ gives the full rotating vector.

### 4. De-emphasis

Transmitters boost the treble before sending (*pre-emphasis*) to improve signal-to-noise. The receiver has to cut it back with a single-pole low-pass filter:

```csharp
var alpha = 1 - Math.Exp(-1 / (250_000 * tau));
state += alpha * (sample - state);
output = state;
```

- `tau` (the time constant) is **50 µs** in the UK and Europe, and **75 µs** in the Americas.
- Without de-emphasis, the audio sounds harsh and hissy.

### 5. Filter to 15 kHz and decimate to 50 kS/s

The discriminator output is not just audio. It is the FM **multiplex (MPX)** signal:

| Frequency | Contents |
|---|---|
| 0 – 15 kHz | Mono audio (L+R) |
| 19 kHz | Stereo pilot tone |
| 23 – 53 kHz | Stereo difference signal (L−R), on a suppressed 38 kHz carrier |
| 57 kHz | RDS data (station name, etc.) |

For mono, **low-pass filter at 15 kHz**, then **decimate by 5**: 250 kS/s becomes 50 kS/s, a perfectly good audio rate.

(If you want exactly 48 kHz, capture at 2.4 MS/s instead: ÷10 gives 240 kS/s, then ÷5 gives 48 kS/s.)

### 6. Write a WAV file

A WAV file is a 44-byte RIFF header followed by raw samples. No library is needed.

- Format: PCM (1), 1 channel, 50,000 Hz, 16 bits per sample.
- Scale each sample from about ±1 to a `short`: `(short)Math.Clamp(sample * 32767, -32768, 32767)`.
- Header fields: `"RIFF"`, file size − 8, `"WAVE"`, `"fmt "` chunk (16 bytes: format, channels, sample rate, byte rate, block align, bits per sample), then `"data"` and the data size.

### Practical points

- **Length:** a single capture is limited to 5 s at 2 MS/s. For longer audio, use continuous recording and process the file in chunks. The filters must keep their state (the last N input samples, the de-emphasis `state`, and the previous IQ sample for the discriminator) from one chunk to the next, or there will be a click at every chunk boundary.
- **Gain:** clicks or noise usually mean the signal is too weak or overloading the receiver. Use the waterfall hover readout:
  - The station should be well above the noise floor.
  - Its peak should stay below about −10 dBFS; close to 0 dBFS means the samples are clipping.
- **Frequency accuracy:** the HackRF's clock is not perfect, so the station may appear a few kHz off. Use the hover readout to find its true centre on the waterfall, and shift by that.

### Things to try next

- [ ] Plot a spectrum of the MPX signal after step 3 and identify the mono audio, the 19 kHz pilot, the stereo band and RDS.
- [ ] Skip de-emphasis and listen to the difference.
- [ ] Skip the low-pass filter in step 2 and listen for aliasing from neighbouring stations.
- [ ] Stereo decoding: lock onto the 19 kHz pilot, double it to recover the 38 kHz carrier, use that to demodulate L−R, then combine: L = (L+R) + (L−R), R = (L+R) − (L−R).
- [ ] RDS decoding: the 57 kHz subcarrier carries data at 1187.5 bit/s. It's a good introduction to digital demodulation.

### Questions / notes

-
