namespace HackRfIqDataTest.Models;

/// <summary>
/// How the radio was set up for a capture or recording. Needed to interpret the samples.
/// </summary>
public sealed record RadioTuning(double CenterFrequencyHz, double SampleRateHz, uint LnaGainDb, uint VgaGainDb, bool AmpEnabled);
