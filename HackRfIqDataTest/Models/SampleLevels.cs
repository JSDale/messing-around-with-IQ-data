namespace HackRfIqDataTest.Models;

/// <summary>
/// How much of the 8-bit range the samples use. <see cref="Rms"/> and <see cref="Peak"/> are in raw sample units, out
/// of 128.
/// <para>
/// The thing to avoid is clipping, which distorts everything. A low RMS is fine: the samples always contain the
/// receiver's own analogue noise, and once that is above about 1 unit the rounding to 8 bits (quantisation noise, about
/// 0.3 units) adds almost nothing. Beyond that, more gain amplifies the noise along with the signal, so judge signal
/// quality by how far a signal stands above the noise floor on the waterfall rather than by RMS.
/// </para>
/// </summary>
public sealed record SampleLevels(double Rms, int Peak, double ClippedPercent)
{
    public string Assessment =>
        this.ClippedPercent > 0.01 ? "clipping: reduce gain" :
        this.Rms < 1 ? "very low: increase gain" :
        "OK";

    public override string ToString() =>
        $"RMS {this.Rms:F1}/128, peak {this.Peak}/128, {this.ClippedPercent:F3}% clipped ({this.Assessment})";
}
