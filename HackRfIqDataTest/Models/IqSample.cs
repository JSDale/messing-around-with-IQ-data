namespace HackRfIqDataTest.Models;

/// <summary>
/// One raw IQ sample as received from the SDR: the in-phase (I) and quadrature (Q) values as signed 8-bit integers.
/// </summary>
public sealed record IqSample(int Index, sbyte I, sbyte Q);
