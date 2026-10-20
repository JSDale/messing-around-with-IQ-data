using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest.Storage;

/// <summary>
/// Writes a SigMF recording (https://sigmf.org): a <c>.sigmf-data</c> file holding the raw samples exactly as the
/// HackRF sends them (SigMF datatype <c>ci8</c>), and a <c>.sigmf-meta</c> JSON file describing them.
/// </summary>
public sealed class SigMfWriter : IAsyncDisposable
{
    public const string DataExtension = ".sigmf-data";
    public const string MetaExtension = ".sigmf-meta";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly FileStream _data;
    private readonly RadioTuning _tuning;
    private readonly DateTime _startedAtUtc;
    private readonly JsonArray _captures = [];
    private long _expectedSampleIndex;

    private SigMfWriter(string basePath, RadioTuning tuning, DateTime startedAtUtc)
    {
        this.DataPath = basePath + DataExtension;
        this.MetaPath = basePath + MetaExtension;
        this._tuning = tuning;
        this._startedAtUtc = startedAtUtc;
        this._data = new FileStream(this.DataPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20, useAsync: true);

        this.AddCaptureSegment(0, startedAtUtc);
    }

    public string DataPath { get; }

    public string MetaPath { get; }

    public long SamplesWritten { get; private set; }

    public long BytesWritten => this.SamplesWritten * 2;

    public int GapCount { get; private set; }

    public static SigMfWriter Create(string directory, RadioTuning tuning, DateTime startedAtUtc)
    {
        Directory.CreateDirectory(directory);
        var name = $"{startedAtUtc:yyyyMMdd-HHmmss}_{tuning.CenterFrequencyHz / 1e6:F3}MHz";
        var basePath = Path.Combine(directory, name);
        for (var suffix = 1; File.Exists(basePath + DataExtension) || File.Exists(basePath + MetaExtension); suffix++)
        {
            basePath = Path.Combine(directory, $"{name}-{suffix}");
        }

        return new SigMfWriter(basePath, tuning, startedAtUtc);
    }

    /// <param name="samples">Interleaved ci8 samples.</param>
    /// <param name="sampleIndex">Position of the first sample in the radio's stream, see <see cref="IqBlock.SampleIndex"/>.</param>
    public async Task WriteAsync(ReadOnlyMemory<byte> samples, long sampleIndex)
    {
        // SigMF marks a discontinuity by starting a new capture segment where the samples resume, with the time they
        // resumed. The data file itself just continues, so it stays a plain sequence of samples.
        if (sampleIndex != this._expectedSampleIndex)
        {
            this.AddCaptureSegment(this.SamplesWritten, this._startedAtUtc + TimeSpan.FromSeconds(sampleIndex / this._tuning.SampleRateHz));
            this.GapCount++;
        }

        await this._data.WriteAsync(samples);
        this.SamplesWritten += samples.Length / 2;
        this._expectedSampleIndex = sampleIndex + samples.Length / 2;
    }

    public async ValueTask DisposeAsync()
    {
        await this._data.DisposeAsync();
        this.WriteMetadata();
    }

    private void AddCaptureSegment(long sampleStart, DateTime startedAtUtc)
    {
        this._captures.Add(new JsonObject
        {
            ["core:sample_start"] = sampleStart,
            ["core:frequency"] = this._tuning.CenterFrequencyHz,
            ["core:datetime"] = startedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
        });

        // Rewritten on every change so a recording that is cut short (crash, power loss) still has valid metadata.
        this.WriteMetadata();
    }

    private void WriteMetadata()
    {
        var metadata = new JsonObject
        {
            ["global"] = new JsonObject
            {
                ["core:datatype"] = "ci8",
                ["core:sample_rate"] = this._tuning.SampleRateHz,
                ["core:version"] = "1.2.0",
                ["core:hw"] = "HackRF One",
                ["core:recorder"] = "HackRfIqDataTest",
                ["core:extensions"] = new JsonArray(new JsonObject
                {
                    ["name"] = "hackrf",
                    ["version"] = "1.0.0",
                    ["optional"] = true,
                }),
                ["hackrf:lna_gain_db"] = this._tuning.LnaGainDb,
                ["hackrf:vga_gain_db"] = this._tuning.VgaGainDb,
                ["hackrf:amp_enabled"] = this._tuning.AmpEnabled,
            },
            ["captures"] = this._captures.DeepClone(),
            ["annotations"] = new JsonArray(),
        };

        File.WriteAllText(this.MetaPath, metadata.ToJsonString(JsonOptions));
    }
}
