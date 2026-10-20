using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using HackRfIqDataTest.Analysis;
using HackRfIqDataTest.Demodulation;
using HackRfIqDataTest.Models;
using HackRfIqDataTest.Storage;

namespace HackRfIqDataTest.ViewModels;

public sealed class AnalysisViewModel : INotifyPropertyChanged
{
    private const int MaxWaterfallRows = 1000;
    // Recordings can be far bigger than memory, so only this much is loaded at once.
    private const int MaxLoadedSamples = 10_000_000;

    private CancellationTokenSource? _waterfallCancellation;
    private PlottedWaterfall? _plotted;

    public string RecordingsDirectory { get; }

    public IReadOnlyList<RecordingSummary> Recordings { get; private set => _ = this.SetField(ref field, value); } = [];

    public RecordingSummary? SelectedRecording
    {
        get;
        set
        {
            if (this.SetField(ref field, value))
            {
                _ = this.UpdateWaterfall();
            }
        }
    }

    public IReadOnlyList<int> FftSizes { get; } = [256, 512, 1024, 2048, 4096];

    public int SelectedFftSize
    {
        get;
        set
        {
            if (this.SetField(ref field, value))
            {
                _ = this.UpdateWaterfall();
            }
        }
    } = 1024;

    public double StartOffsetSeconds { get; set => _ = this.SetField(ref field, value); }

    public double LengthMs { get; set => _ = this.SetField(ref field, value); } = 1000;

    public Bitmap? WaterfallImage { get; private set => _ = this.SetField(ref field, value); }

    public string Details { get; private set => _ = this.SetField(ref field, value); } = "Select a capture to plot";

    public string LowFrequencyLabel { get; private set => _ = this.SetField(ref field, value); } = "";

    public string CenterFrequencyLabel { get; private set => _ = this.SetField(ref field, value); } = "";

    public string HighFrequencyLabel { get; private set => _ = this.SetField(ref field, value); } = "";

    public string StartTimeLabel { get; private set => _ = this.SetField(ref field, value); } = "";

    public string EndTimeLabel { get; private set => _ = this.SetField(ref field, value); } = "";

    public IReadOnlyList<DemodulationMode> DemodulationModes => DemodulationMode.All;

    public DemodulationMode SelectedDemodulation { get; set => _ = this.SetField(ref field, value); } = DemodulationMode.All[0];

    /// <summary>Signal frequency minus the recording's centre frequency, as shown by the waterfall hover readout.</summary>
    public double SignalOffsetKHz { get; set => _ = this.SetField(ref field, value); }

    public string ExportStatus { get; private set => _ = this.SetField(ref field, value); } = "";

    public ICommand RefreshRecordings { get; }

    public ICommand Export { get; }

    public ICommand Plot { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AnalysisViewModel(string recordingsDirectory)
    {
        this.RecordingsDirectory = recordingsDirectory;
        this.RefreshRecordings = new AsyncRelayCommand(this.RefreshAsync);
        this.Plot = new AsyncRelayCommand(this.UpdateWaterfall);
        this.Export = new AsyncRelayCommand(this.OnExportRequested);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var selectedName = this.SelectedRecording?.Name;
            this.Recordings = await Task.Run(() => RecordingLibrary.List(this.RecordingsDirectory));
            this.SelectedRecording = this.Recordings.FirstOrDefault(r => r.Name == selectedName);
        }
        catch (Exception ex)
        {
            this.Details = $"Failed to read recordings: {ex.Message}";
        }
    }

    private async Task UpdateWaterfall()
    {
        this._waterfallCancellation?.Cancel();
        var cancellation = this._waterfallCancellation = new CancellationTokenSource();

        var recording = this.SelectedRecording;
        if (recording is null)
        {
            this.WaterfallImage = null;
            this._plotted = null;
            return;
        }

        try
        {
            this.Details = $"Loading {recording.Name}...";
            var startSample = (long)(this.StartOffsetSeconds * recording.Tuning.SampleRateHz);
            var requestedSamples = (long)(this.LengthMs / 1000 * recording.Tuning.SampleRateHz);
            var capture = await RecordingLibrary.LoadAsync(
                recording, startSample, (int)Math.Clamp(requestedSamples, 1, MaxLoadedSamples), cancellation.Token);

            var fftSize = this.SelectedFftSize;
            var data = await Task.Run(
                () => Waterfall.Compute(capture, fftSize, MaxWaterfallRows, cancellation.Token), cancellation.Token);


            var halfSpanMHz = capture.SampleRateHz / 2 / 1e6;
            var centerMHz = capture.CenterFrequencyHz / 1e6;
            this.LowFrequencyLabel = $"{centerMHz - halfSpanMHz:F3} MHz";
            this.CenterFrequencyLabel = $"{centerMHz:F3} MHz";
            this.HighFrequencyLabel = $"{centerMHz + halfSpanMHz:F3} MHz";

            var rowDurationMs = data.FramesPerRow * fftSize / capture.SampleRateHz * 1000;
            var startSeconds = startSample / capture.SampleRateHz;
            this.WaterfallImage = WaterfallRenderer.Render(data);
            this._plotted = new PlottedWaterfall(data, capture.CenterFrequencyHz, capture.SampleRateHz, startSeconds, rowDurationMs / 1000);
            this.StartTimeLabel = $"{startSeconds:F3} s";
            this.EndTimeLabel = $"{startSeconds + data.Height * rowDurationMs / 1000:F3} s";
            this.Details =
                (requestedSamples > MaxLoadedSamples ? $"Length limited to {MaxLoadedSamples:N0} samples. " : "") +
                (recording.GapCount > 0 ? $"Recording has {recording.GapCount} gap(s) from dropped samples, so the time axis is approximate. " : "") +
                $"Bin width {capture.SampleRateHz / fftSize / 1e3:F2} kHz, " +
                $"{data.Height} rows of {rowDurationMs:F3} ms ({data.FramesPerRow} FFT(s) averaged per row), " +
                $"colour range {data.FloorDb:F1} to {data.PeakDb:F1} dBFS";
        }
        catch (OperationCanceledException)
        {
            // A newer selection replaced this one.
        }
        catch (Exception ex)
        {
            this.Details = $"Failed to plot {recording.Name}: {ex.Message}";
        }
    }

    private async Task OnExportRequested()
    {
        if (this.SelectedRecording is not { } recording)
        {
            this.ExportStatus = "Select a recording to export";
            return;
        }

        var mode = this.SelectedDemodulation;
        var offsetHz = this.SignalOffsetKHz * 1e3;
        var path = Path.Combine(this.RecordingsDirectory, $"{recording.Name}_{mode.FileTag}_{this.SignalOffsetKHz:0.###}kHz.wav");
        try
        {
            var demodulator = mode.Create(recording.Tuning.SampleRateHz, offsetHz);
            var progress = new Progress<double>(fraction => this.ExportStatus = $"Exporting... {fraction:P0}");
            await Task.Run(() => AudioExporter.ExportAsync(recording, demodulator, path, progress, CancellationToken.None));
            this.ExportStatus =
                $"Exported {(recording.Tuning.CenterFrequencyHz + offsetHz) / 1e6:F3} MHz as {demodulator.AudioSampleRate} Hz audio to {path}";
        }
        catch (Exception ex)
        {
            this.ExportStatus = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Describes the point under the cursor, given as fractions (0-1) of the plot's width and height.
    /// Returns null when nothing is plotted.
    /// </summary>
    public string? DescribePoint(double xFraction, double yFraction)
    {
        if (this._plotted is not { } plot)
        {
            return null;
        }

        var data = plot.Data;
        var column = Math.Clamp((int)(xFraction * data.Width), 0, data.Width - 1);
        var row = Math.Clamp((int)(yFraction * data.Height), 0, data.Height - 1);

        // Each column is one FFT bin; after the fftshift, column Width/2 is the centre frequency.
        var binWidthHz = plot.SampleRateHz / data.Width;
        var offsetHz = (column - data.Width / 2) * binWidthHz;
        var timeSeconds = plot.StartSeconds + row * plot.RowSeconds;

        return $"{(plot.CenterFrequencyHz + offsetHz) / 1e6:F4} MHz ({offsetHz / 1e3:+0.0;-0.0;0.0} kHz from centre)\n" +
               $"{timeSeconds:F4} s\n" +
               $"{data.PowerDb[row * data.Width + column]:F1} dBFS";
    }

    private sealed record PlottedWaterfall(
        WaterfallData Data, double CenterFrequencyHz, double SampleRateHz, double StartSeconds, double RowSeconds);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }
}
