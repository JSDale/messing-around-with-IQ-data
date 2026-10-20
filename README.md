# HackRfIqDataTest

An Avalonia desktop app that captures IQ data from a HackRF One. Captures and continuous recordings are saved as [SigMF](https://sigmf.org) files so the data can be analysed.

## Prerequisites

- [.NET 10 SDK](https://learn.microsoft.com/dotnet/core/install/linux-ubuntu)
- A HackRF One connected by USB
- **libhackrf**, the native HackRF library. The app calls it through P/Invoke and does not bundle it, so you must install it separately.

### Installing libhackrf on Ubuntu

```bash
sudo apt update
sudo apt install hackrf libhackrf0
```

- `libhackrf0` provides `libhackrf.so.0`, which the app loads at runtime.
- `hackrf` provides the command-line tools (`hackrf_info`, `hackrf_transfer`, ...) and udev rules that let members of the `plugdev` group access the device without root.

Check that the device is detected:

```bash
hackrf_info
```

If `hackrf_info` only works with `sudo`, add yourself to `plugdev`, then log out and back in:

```bash
sudo usermod -aG plugdev $USER
```

If `hackrf_info` reports a firmware or library version mismatch, update the device firmware. For firmware updates and installation on other platforms, see the official HackRF documentation:
https://hackrf.readthedocs.io/en/latest/installing_hackrf_software.html

## Building and running

```bash
dotnet run --project HackRfIqDataTest/HackRfIqDataTest.csproj
```

Enter a start and end frequency in MHz. The radio is centred on the midpoint of the two frequencies, and the sample rate matches the span (2 MHz minimum, 20 MHz maximum). If libhackrf is missing or no device is connected, the error is shown in the log panel.

### Receiver gain

The HackRF has three gain stages, set under **Receiver gain** and applied to every capture and recording:

- **LNA** (RF low-noise amplifier): 0–40 dB in 8 dB steps. Raise this first for weak signals.
- **VGA** (baseband amplifier): 0–62 dB in 2 dB steps. Use it to fill the 8-bit sample range.
- **Amp**: an extra 14 dB at the antenna input. Helps weak signals, but strong nearby signals can overload it.

After each capture the log shows the sample **levels**: RMS and peak (out of 128) and the percentage of samples clipped at ±127. Aim for an RMS of roughly 20–40 with no clipping. "low" means increase the gain; "clipping" means reduce it. For FM broadcast on the stock antenna, LNA 32 and VGA 30 is a reasonable starting point.

### Single capture

Enter a capture duration in milliseconds and click **capture data**. The raw I/Q values are shown in the table. A capture is limited to 10 million samples (500 ms at 20 MS/s, 5 s at 2 MS/s). Click **save to file** to save it as a recording.

### Continuous recording

Click **start recording** to stream samples straight to disk, and **stop recording** to finish. The status line shows elapsed time, data written and any dropped samples.

Raw IQ data is large: 2 bytes per sample, so 4 MB/s (14 GB/hour) at 2 MS/s and 40 MB/s (144 GB/hour) at 20 MS/s. Recording stops by itself if free disk space drops below 1 GB.

If the disk cannot keep up, samples are dropped rather than stalling the radio. The recording continues and the gap is marked in the metadata (see below).

## Recording files

Recordings are saved in the folder set by `Recordings:Directory` in `appsettings.json` (next to the executable). The default is `~/HackRfRecordings`:

```json
{
  "Recordings": {
    "Directory": "~/HackRfRecordings"
  }
}
```

A leading `~` means your home folder, and a relative path is relative to the executable. If the setting or file is missing, recordings go to `~/.local/share/HackRfIqDataTest/recordings/`. Restart the app after changing it.

Each recording is a pair of [SigMF](https://sigmf.org) files named after the start time and centre frequency:

- `<name>.sigmf-data` holds the raw samples exactly as the HackRF sends them: interleaved signed 8-bit I and Q values (`I0, Q0, I1, Q1, ...`). SigMF calls this format `ci8`.
- `<name>.sigmf-meta` is JSON describing them: sample rate, centre frequency, start time, gains and hardware.

If samples were dropped, the metadata has more than one entry in `captures`. Each entry gives the sample position in the data file where the samples resume and the time they resumed.

Because SigMF is a standard format, other tools can open the recordings, for example [inspectrum](https://github.com/miek/inspectrum), GNU Radio, or Python with NumPy:

```python
import numpy as np
raw = np.fromfile("recording.sigmf-data", dtype=np.int8)
iq = (raw[0::2] + 1j * raw[1::2]) / 128.0
```

## Analysis

The **Analysis** tab lists the recordings in the recordings folder. Select one to plot it as a waterfall. **Start s** and **Length ms** choose which part of the recording to plot (up to 10 million samples at a time); click **plot** after changing them.

- The horizontal axis is frequency, from centre - sample rate/2 to centre + sample rate/2.
- The vertical axis is time, with the earliest samples at the top.
- Colour shows power in dBFS, from black (noise floor) through blue, green and yellow to red (strongest signal).

With **auto colour range** on, the colours stretch from the plot's noise floor to its strongest point, which makes even a weak signal look bright. Turn it off and set **Black at dBFS** and **Red at dBFS** to see true signal strength, or to compare plots on the same scale. The details line above the plot shows the noise floor, peak and sample levels.

The capture is split into blocks of *FFT size* samples. Each block is multiplied by a Hann window and converted to a power spectrum with an FFT (`Analysis/Fft.cs`, `Analysis/Waterfall.cs`). A larger FFT size gives finer frequency resolution (bin width = sample rate / FFT size) but fewer rows in time. If a capture would give more than 1000 rows, neighbouring spectra are averaged together.

A spike at the centre frequency is normal for the HackRF: it is the receiver's DC offset, not a real signal.

## Exporting audio

On the **Analysis** tab, select a recording, choose a **Demodulation** type, enter the signal's **Offset kHz** (signal frequency minus the recording's centre frequency, which the waterfall hover readout shows) and click **export**. The whole recording is demodulated to a 16-bit mono WAV file in the recordings folder.

Available demodulation types:

- **FM broadcast (mono)**: shifts the station to 0 Hz, filters out a 200 kHz channel, FM-demodulates it, applies 50 µs de-emphasis (UK/Europe) and keeps the audio below 15 kHz. The steps are explained in `learning-journal.md` and implemented in `Demodulation/FmBroadcastDemodulator.cs`.

Tip: tune so the station is off-centre (for example 106.5 – 108.5 MHz for a station at 107.2 MHz, giving an offset of -300 kHz), so it does not sit on the HackRF's DC spike.

