using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using HackRfIqDataTest.Models;
using HackRfIqDataTest.Storage;

namespace HackRfIqDataTest.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISoftwareDefineRadio _radio;
    private readonly string _recordingsDirectory;
    private readonly AsyncRelayCommand _captureData;
    private readonly AsyncRelayCommand _saveCapture;
    private readonly AsyncRelayCommand _startRecording;
    private readonly RelayCommand _stopRecording;
    private IqCapture? _lastCapture;
    private bool _lastCaptureSaved;
    private CancellationTokenSource? _recordingCancellation;

    public double StartFrequency { get; set => _ = this.SetField(ref field, value); } = 100;
    
    public double EndFrequency { get; set => _ = this.SetField(ref field, value); } = 102;

    public double CaptureDurationMs { get; set => _ = this.SetField(ref field, value); } = 100;

    public IReadOnlyList<uint> LnaGains { get; } = [0, 8, 16, 24, 32, 40];

    public IReadOnlyList<uint> VgaGains { get; } = Enumerable.Range(0, 32).Select(step => (uint)step * 2).ToList();

    public uint LnaGainDb { get; set => _ = this.SetField(ref field, value); } = 16;

    public uint VgaGainDb { get; set => _ = this.SetField(ref field, value); } = 20;

    public bool AmpEnabled { get; set => _ = this.SetField(ref field, value); }

    public string RecordingStatus { get; private set => _ = this.SetField(ref field, value); } = "Not recording";

    public ICommand CaptureData => this._captureData;

    public ICommand SaveCapture => this._saveCapture;

    public ICommand StartRecording => this._startRecording;

    public ICommand StopRecording => this._stopRecording;

    public IReadOnlyList<IqSample> Samples { get; private set => _ = this.SetField(ref field, value); } = [];

    public string CaptureDetails { get; private set => _ = this.SetField(ref field, value); } = "No capture yet";

    public UserLog UserLog { get; }

    public AnalysisViewModel Analysis { get; }

    public ICommand ClearLog { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(UserLog userUserLog, ISoftwareDefineRadio radio, string recordingsDirectory, AnalysisViewModel analysis)
    {
        this.UserLog = userUserLog;
        this._radio = radio;
        this._recordingsDirectory = recordingsDirectory;
        this.Analysis = analysis;
        this._captureData = new AsyncRelayCommand(this.OnCaptureDataRequested, this.IsRadioIdle);
        this._saveCapture = new AsyncRelayCommand(this.OnSaveCaptureRequested, () => this._lastCapture is not null && !this._lastCaptureSaved);
        this._startRecording = new AsyncRelayCommand(this.OnStartRecordingRequested, this.IsRadioIdle);
        this._stopRecording = new RelayCommand(this.OnStopRecordingRequested, () => this._recordingCancellation is not null);
        this.ClearLog = new RelayCommand(this.OnClearLog);
    }

    // The HackRF can only be opened once, so captures and recordings are mutually exclusive.
    private bool IsRadioIdle() => !this._captureData.IsRunning && !this._startRecording.IsRunning;

    private void OnClearLog()
    {
        this.UserLog.Clear();
    }

    private async Task OnCaptureDataRequested()
    {
        this.UserLog.Log.Add($"Capturing {this.StartFrequency} - {this.EndFrequency} MHz for {this.CaptureDurationMs} ms...");
        this.NotifyCommandsChanged();
        try
        {
            this.TuneRadio();
            var capture = await this._radio.StartCapture(TimeSpan.FromMilliseconds(this.CaptureDurationMs));
            this._lastCapture = capture;
            this._lastCaptureSaved = false;
            this.Samples = capture.ToIqSamples();
            var levels = capture.GetLevels();
            this.CaptureDetails =
                $"{capture.CapturedAtUtc:u}  |  Centre {capture.CenterFrequencyHz / 1e6:F3} MHz  |  " +
                $"{capture.SampleRateHz / 1e6:F1} MS/s  |  {capture.SampleCount:N0} samples  |  " +
                $"LNA {capture.Tuning.LnaGainDb} dB, VGA {capture.Tuning.VgaGainDb} dB, Amp {(capture.Tuning.AmpEnabled ? "on" : "off")}\n" +
                $"Levels: {levels}";

            var meanPower = capture.ToComplex().Average(sample => sample.Real * sample.Real + sample.Imaginary * sample.Imaginary);
            this.UserLog.Log.Add(
                $"Captured {capture.SampleCount:N0} samples at {capture.CenterFrequencyHz / 1e6:F3} MHz, " +
                $"{capture.SampleRateHz / 1e6:F1} MS/s, mean power {10 * Math.Log10(meanPower):F1} dBFS");
            this.UserLog.Log.Add($"Levels: {levels}");
        }
        catch (Exception ex)
        {
            this.UserLog.Log.Add($"Capture failed: {ex.Message}");
        }
        finally
        {
            // IsRunning only clears after this method returns, so let the commands re-check once it has.
            Avalonia.Threading.Dispatcher.UIThread.Post(this.NotifyCommandsChanged);
        }
    }

    private async Task OnSaveCaptureRequested()
    {
        try
        {
            var path = await RecordingLibrary.SaveAsync(this._lastCapture!, this._recordingsDirectory);
            this._lastCaptureSaved = true;
            this.UserLog.Log.Add($"Saved capture to {path}");
            await this.Analysis.RefreshAsync();
        }
        catch (Exception ex)
        {
            this.UserLog.Log.Add($"Save failed: {ex.Message}");
        }

        this._saveCapture.NotifyCanExecuteChanged();
    }

    private async Task OnStartRecordingRequested()
    {
        using var cancellation = this._recordingCancellation = new CancellationTokenSource();
        this.NotifyCommandsChanged();
        try
        {
            this.TuneRadio();
            this.RecordingStatus = "Starting...";
            this.UserLog.Log.Add($"Recording {this.StartFrequency} - {this.EndFrequency} MHz...");

            var progress = new Progress<RecordingProgress>(p => this.RecordingStatus =
                $"Recording: {p.Elapsed:hh\\:mm\\:ss}, {p.BytesWritten / 1e6:F1} MB written, {p.DroppedSamples:N0} samples dropped");
            var result = await Recorder.RecordAsync(this._radio, this._recordingsDirectory, progress, cancellation.Token);

            this.RecordingStatus = "Not recording";
            this.UserLog.Log.Add(
                $"Recorded {result.SamplesWritten:N0} samples ({result.SamplesWritten * 2 / 1e6:F1} MB) to {result.MetaPath}" +
                (result.GapCount > 0 ? $"; {result.DroppedSamples:N0} samples dropped in {result.GapCount} gap(s)" : "") +
                (result.StoppedForLowDiskSpace ? "; stopped because the disk is nearly full" : ""));
        }
        catch (OperationCanceledException)
        {
            this.RecordingStatus = "Not recording";
            this.UserLog.Log.Add("Recording stopped before the HackRF started streaming");
        }
        catch (Exception ex)
        {
            this.RecordingStatus = "Not recording";
            this.UserLog.Log.Add($"Recording failed: {ex.Message}");
        }
        finally
        {
            this._recordingCancellation = null;
            Avalonia.Threading.Dispatcher.UIThread.Post(this.NotifyCommandsChanged);
        }

        await this.Analysis.RefreshAsync();
    }

    private void OnStopRecordingRequested()
    {
        this._recordingCancellation?.Cancel();
    }

    private void TuneRadio()
    {
        this._radio.SetStartFrequency(this.StartFrequency * 1e6);
        this._radio.SetEndFrequency(this.EndFrequency * 1e6);
        this._radio.SetLnaGain(this.LnaGainDb);
        this._radio.SetVgaGain(this.VgaGainDb);
        this._radio.SetAmpEnabled(this.AmpEnabled);
    }

    private void NotifyCommandsChanged()
    {
        this._captureData.NotifyCanExecuteChanged();
        this._saveCapture.NotifyCanExecuteChanged();
        this._startRecording.NotifyCanExecuteChanged();
        this._stopRecording.NotifyCanExecuteChanged();
    }

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
