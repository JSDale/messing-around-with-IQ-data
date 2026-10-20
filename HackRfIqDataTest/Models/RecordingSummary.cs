using System;

namespace HackRfIqDataTest.Models;

/// <summary>
/// A SigMF recording on disk, described by its metadata file. The samples themselves are not loaded.
/// </summary>
/// <param name="SegmentCount">Number of SigMF capture segments. More than one means samples were dropped and the
/// recording has gaps.</param>
public sealed record RecordingSummary(
    string Name,
    string MetaPath,
    string DataPath,
    DateTime StartedAtUtc,
    RadioTuning Tuning,
    long SampleCount,
    int SegmentCount)
{
    public double CenterFrequencyMHz => this.Tuning.CenterFrequencyHz / 1e6;

    public double SampleRateMsps => this.Tuning.SampleRateHz / 1e6;

    public double DurationSeconds => this.SampleCount / this.Tuning.SampleRateHz;

    public double SizeMB => this.SampleCount * 2 / 1e6;

    public int GapCount => this.SegmentCount - 1;
}
