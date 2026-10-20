namespace HackRfIqDataTest.Models;

/// <summary>
/// How much of the 8-bit range the samples use. <see cref="Rms"/> and <see cref="Peak"/> are in raw sample units, out
/// of 128. Too low and weak signals are lost in the noise; too high and the samples clip, which distorts everything.
/// </summary>
public sealed record SampleLevels(double Rms, int Peak, double ClippedPercent)
{
    public string Assessment =>
        this.ClippedPercent > 0.01 ? "clipping: reduce gain" :
        this.Rms < 8 ? "low: increase gain" :
        "OK";

    public override string ToString() =>
        $"RMS {this.Rms:F1}/128, peak {this.Peak}/128, {this.ClippedPercent:F3}% clipped ({this.Assessment})";
}
