using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HackRfIqDataTest.Storage;

public sealed record RecordingProgress(TimeSpan Elapsed, long BytesWritten, long DroppedSamples);

public sealed record RecordingResult(string MetaPath, long SamplesWritten, long DroppedSamples, int GapCount);

/// <summary>
/// Streams from the radio straight into a SigMF recording between a start and stop time.
/// </summary>
public static class Recorder
{
    private static readonly TimeSpan NoDataTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Waits until <paramref name="startAtUtc"/>, then records until <paramref name="stopAtUtc"/>. Cancelling before
    /// the start time abandons the recording; cancelling after it stops early and keeps what was recorded.
    /// </summary>
    public static async Task<RecordingResult> RecordAsync(
        ISoftwareDefineRadio radio,
        string directory,
        DateTime startAtUtc,
        DateTime stopAtUtc,
        IProgress<RecordingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (stopAtUtc <= startAtUtc || stopAtUtc <= DateTime.UtcNow)
        {
            throw new ArgumentException("Stop time must be after the start time and in the future.");
        }

        var wait = startAtUtc - DateTime.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        await using var stream = await radio.StartStreaming(cancellationToken);
        EnsureFreeSpace(directory, (long)(stream.Tuning.SampleRateHz * 2 * (stopAtUtc - DateTime.UtcNow).TotalSeconds));
        await using var writer = SigMfWriter.Create(directory, stream.Tuning, stream.StartedAtUtc);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(TimeSpan.FromTicks(Math.Max((stopAtUtc - DateTime.UtcNow).Ticks, 0)));

        var sinceProgress = Stopwatch.StartNew();
        while (true)
        {
            using var noData = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            noData.CancelAfter(NoDataTimeout);

            IqBlock block;
            try
            {
                block = await stream.Blocks.ReadAsync(noData.Token);
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                throw new TimeoutException($"The HackRF sent no data for {NoDataTimeout.TotalSeconds:F0} s. The recording so far has been kept.");
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                // Reached the stop time, or stopped early.
                break;
            }

            using (block)
            {
                await writer.WriteAsync(block.Data, block.SampleIndex);
            }

            if (sinceProgress.Elapsed >= ProgressInterval)
            {
                progress?.Report(new RecordingProgress(DateTime.UtcNow - stream.StartedAtUtc, writer.BytesWritten, stream.DroppedSamples));
                sinceProgress.Restart();
            }
        }

        return new RecordingResult(writer.MetaPath, writer.SamplesWritten, stream.DroppedSamples, writer.GapCount);
    }

    private static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        Directory.CreateDirectory(directory);
        var fullPath = Path.GetFullPath(directory);
        var drive = DriveInfo.GetDrives()
            .Where(d => d.IsReady && fullPath.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
            .MaxBy(d => d.RootDirectory.FullName.Length);

        if (drive is not null && drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(
                $"The recording needs about {requiredBytes / 1e9:F1} GB but only {drive.AvailableFreeSpace / 1e9:F1} GB is free.");
        }
    }
}
