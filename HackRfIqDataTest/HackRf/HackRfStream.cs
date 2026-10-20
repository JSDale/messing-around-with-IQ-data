using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest.HackRf;

/// <summary>
/// Streams samples from an open HackRF until disposed. libhackrf calls back on its USB thread for every transfer; the
/// callback only copies the data into a queue, because blocking there would overflow the device's buffers.
/// </summary>
internal sealed unsafe class HackRfStream : IIqStream
{
    // libhackrf transfers are 256 KiB, so this is 128 MiB: about 3 s of buffering at 20 MS/s.
    private const int MaxQueuedBlocks = 512;

    private readonly Channel<IqBlock> _blocks = Channel.CreateBounded<IqBlock>(
        new BoundedChannelOptions(MaxQueuedBlocks) { SingleReader = true, SingleWriter = true });

    private GCHandle _handle;
    private nint _device;
    private long _receivedSamples;
    private long _droppedSamples;
    private int _disposed;

    private HackRfStream(RadioTuning tuning)
    {
        this.Tuning = tuning;
        this._handle = GCHandle.Alloc(this);
    }

    public RadioTuning Tuning { get; }

    public DateTime StartedAtUtc { get; private set; }

    public ChannelReader<IqBlock> Blocks => this._blocks.Reader;

    public long DroppedSamples => Interlocked.Read(ref this._droppedSamples);

    /// <summary>
    /// Opens the first HackRF found, applies <paramref name="tuning"/> and starts receiving. Blocks while the device
    /// is opened, so call it off the UI thread.
    /// </summary>
    public static HackRfStream Start(RadioTuning tuning)
    {
        HackRfNative.ThrowIfError(HackRfNative.Init(), "hackrf_init");
        var stream = new HackRfStream(tuning);
        try
        {
            stream.Open();
            return stream;
        }
        catch
        {
            stream.Shutdown();
            throw;
        }
    }

    public ValueTask DisposeAsync() => new(Task.Run(this.Shutdown));

    private void Open()
    {
        HackRfNative.ThrowIfError(HackRfNative.Open(out this._device), "hackrf_open");
        HackRfNative.ThrowIfError(HackRfNative.SetSampleRate(this._device, this.Tuning.SampleRateHz), "hackrf_set_sample_rate");
        var filterBandwidthHz = HackRfNative.ComputeBasebandFilterBandwidth((uint)(this.Tuning.SampleRateHz * 0.75));
        HackRfNative.ThrowIfError(HackRfNative.SetBasebandFilterBandwidth(this._device, filterBandwidthHz), "hackrf_set_baseband_filter_bandwidth");
        HackRfNative.ThrowIfError(HackRfNative.SetFrequency(this._device, (ulong)this.Tuning.CenterFrequencyHz), "hackrf_set_freq");
        HackRfNative.ThrowIfError(HackRfNative.SetLnaGain(this._device, this.Tuning.LnaGainDb), "hackrf_set_lna_gain");
        HackRfNative.ThrowIfError(HackRfNative.SetVgaGain(this._device, this.Tuning.VgaGainDb), "hackrf_set_vga_gain");
        HackRfNative.ThrowIfError(HackRfNative.SetAmpEnable(this._device, (byte)(this.Tuning.AmpEnabled ? 1 : 0)), "hackrf_set_amp_enable");

        this.StartedAtUtc = DateTime.UtcNow;
        HackRfNative.ThrowIfError(
            HackRfNative.StartRx(this._device, &OnSamplesReceived, (void*)GCHandle.ToIntPtr(this._handle)),
            "hackrf_start_rx");
    }

    private void Shutdown()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
        {
            return;
        }

        // hackrf_close joins the libusb transfer thread, so the callback can no longer run after it.
        if (this._device != 0)
        {
            HackRfNative.StopRx(this._device);
            HackRfNative.Close(this._device);
            this._device = 0;
        }

        this._handle.Free();
        HackRfNative.Exit();
        this._blocks.Writer.TryComplete();
    }

    private void Receive(ReadOnlySpan<byte> data)
    {
        var block = IqBlock.Copy(data, this._receivedSamples);
        if (!this._blocks.Writer.TryWrite(block))
        {
            block.Dispose();
            Interlocked.Add(ref this._droppedSamples, block.SampleCount);
        }

        this._receivedSamples += block.SampleCount;
    }

    // Called by libhackrf on its USB transfer thread. Returning non-zero tells it to stop streaming.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnSamplesReceived(HackRfTransfer* transfer)
    {
        try
        {
            var stream = (HackRfStream)GCHandle.FromIntPtr((nint)transfer->RxContext).Target!;
            stream.Receive(new ReadOnlySpan<byte>(transfer->Buffer, transfer->ValidLength));
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}
