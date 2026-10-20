using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest.Storage;

/// <summary>
/// Saves, lists and reads SigMF recordings in a folder.
/// </summary>
public static class RecordingLibrary
{
    /// <summary>
    /// ~/.local/share/HackRfIqDataTest/recordings on Linux.
    /// </summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HackRfIqDataTest", "recordings");

    public static async Task<string> SaveAsync(IqCapture capture, string directory)
    {
        await using var writer = SigMfWriter.Create(directory, capture.Tuning, capture.CapturedAtUtc);
        await writer.WriteAsync(capture.Samples, 0);
        return writer.MetaPath;
    }

    /// <summary>
    /// Lists the <c>ci8</c> recordings in <paramref name="directory"/>, newest first. Files that cannot be read or use
    /// another sample format are skipped.
    /// </summary>
    public static IReadOnlyList<RecordingSummary> List(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var recordings = new List<RecordingSummary>();
        foreach (var metaPath in Directory.EnumerateFiles(directory, "*" + SigMfWriter.MetaExtension))
        {
            try
            {
                if (ReadSummary(metaPath) is { } summary)
                {
                    recordings.Add(summary);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException or NullReferenceException)
            {
                // Not a recording this app can read.
            }
        }

        return recordings.OrderByDescending(r => r.StartedAtUtc).ToList();
    }

    /// <summary>
    /// Reads up to <paramref name="sampleCount"/> samples starting at <paramref name="startSample"/>. The file is
    /// opened shared, so a recording can be read while it is still being written.
    /// </summary>
    public static async Task<IqCapture> LoadAsync(
        RecordingSummary recording, long startSample, int sampleCount, CancellationToken cancellationToken = default)
    {
        var available = new FileInfo(recording.DataPath).Length / 2 - startSample;
        if (startSample < 0 || available <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSample), "Start is outside the recording.");
        }

        var samples = new byte[Math.Min(sampleCount, available) * 2];
        using var file = File.OpenHandle(recording.DataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.Asynchronous);
        var read = 0;
        while (read < samples.Length)
        {
            var count = await RandomAccess.ReadAsync(file, samples.AsMemory(read), startSample * 2 + read, cancellationToken);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        return new IqCapture
        {
            Tuning = recording.Tuning,
            CapturedAtUtc = recording.StartedAtUtc + TimeSpan.FromSeconds(startSample / recording.Tuning.SampleRateHz),
            Samples = read == samples.Length ? samples : samples[..read],
        };
    }

    private static RecordingSummary? ReadSummary(string metaPath)
    {
        var metadata = JsonNode.Parse(File.ReadAllText(metaPath))!;
        var global = metadata["global"]!;
        if ((string?)global["core:datatype"] != "ci8")
        {
            return null;
        }

        var dataPath = Path.ChangeExtension(metaPath, SigMfWriter.DataExtension);
        var captures = metadata["captures"]?.AsArray() ?? [];
        var first = captures.FirstOrDefault();
        var startedAtUtc = (string?)first?["core:datetime"] is { } datetime
            ? DateTime.Parse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : File.GetLastWriteTimeUtc(dataPath);

        var tuning = new RadioTuning(
            (double?)first?["core:frequency"] ?? 0,
            (double)global["core:sample_rate"]!,
            (uint?)global["hackrf:lna_gain_db"] ?? 0,
            (uint?)global["hackrf:vga_gain_db"] ?? 0,
            (bool?)global["hackrf:amp_enabled"] ?? false);

        return new RecordingSummary(
            Path.GetFileNameWithoutExtension(metaPath),
            metaPath,
            dataPath,
            startedAtUtc,
            tuning,
            new FileInfo(dataPath).Length / 2,
            Math.Max(captures.Count, 1));
    }
}
