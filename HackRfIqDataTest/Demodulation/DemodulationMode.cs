using System;
using System.Collections.Generic;

namespace HackRfIqDataTest.Demodulation;

/// <param name="FileTag">Short name used in exported file names.</param>
/// <param name="Create">Creates a demodulator for a recording's sample rate and the signal's offset from its centre.</param>
public sealed record DemodulationMode(string Name, string FileTag, Func<double, double, IDemodulator> Create)
{
    public static IReadOnlyList<DemodulationMode> All { get; } =
    [
        new("FM broadcast (mono)", "fm", (sampleRateHz, offsetHz) => new FmBroadcastDemodulator(sampleRateHz, offsetHz)),
    ];

    public override string ToString() => this.Name;
}
