using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HackRfIqDataTest.Storage;

public sealed record RecordingProgress(TimeSpan Elapsed, long BytesWritten, long DroppedSamples);

public sealed record RecordingResult(string MetaPath, long SamplesWritten, long DroppedSamples, int GapCount, bool StoppedForLowDiskSpace);

/// <summary>
/// Streams from the radio straight into a SigMF recording until cancelled.
/// </summary>
public static class Recorder
{
    private const long MinFreeBytes = 1_000_000_000;
    private static readonly TimeSpan NoDataTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Records until <paramref name="cancellationToken"/> is cancelled, keeping everything recorded up to that point.
    /// Also stops by itself if the disk gets close to full.
    /// </summary>
    public static async Task<RecordingResult> RecordAsync(
        ISoftwareDefineRadio radio, string directory, IProgress<RecordingProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var drive = FindDrive(directory);
        ThrowIfLowDiskSpace(drive);

        await using var stream = await radio.StartStreaming(cancellationToken);
        await using var writer = SigMfWriter.Create(directory, stream.Tuning, stream.StartedAtUtc);

        var stoppedForLowDiskSpace = false;
        var sinceProgress = Stopwatch.StartNew();
        while (!stoppedForLowDiskSpace)
        {
            using var noData = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            noData.CancelAfter(NoDataTimeout);

            IqBlock block;
            try
            {
                block = await stream.Blocks.ReadAsync(noData.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The HackRF sent no data for {NoDataTimeout.TotalSeconds:F0} s. The recording so far has been kept.");
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                // Stopped by the user.
                break;
            }

            using (block)
            {
                await writer.WriteAsync(block.Data, block.SampleIndex);
            }

            if (sinceProgress.Elapsed >= ProgressInterval)
            {
                progress?.Report(new RecordingProgress(DateTime.UtcNow - stream.StartedAtUtc, writer.BytesWritten, stream.DroppedSamples));
                stoppedForLowDiskSpace = drive is not null && drive.AvailableFreeSpace < MinFreeBytes;
                sinceProgress.Restart();
            }
        }

        return new RecordingResult(writer.MetaPath, writer.SamplesWritten, stream.DroppedSamples, writer.GapCount, stoppedForLowDiskSpace);
    }

    private static DriveInfo? FindDrive(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady && fullPath.StartsWith(d.RootDirectory.FullName, StringComparison.Ordinal))
            .MaxBy(d => d.RootDirectory.FullName.Length);
    }

    private static void ThrowIfLowDiskSpace(DriveInfo? drive)
    {
        if (drive is not null && drive.AvailableFreeSpace < MinFreeBytes)
        {
            throw new IOException($"Only {drive.AvailableFreeSpace / 1e9:F1} GB of disk space is free; at least {MinFreeBytes / 1e9:F0} GB is needed to record.");
        }
    }
}
