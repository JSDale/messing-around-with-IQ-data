using System;
using System.Threading;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest;

public interface ISoftwareDefineRadio
{
    void SetStartFrequency(double startFrequencyHz);
    
    void SetEndFrequency(double endFrequencyHz);

    /// <summary>RF low-noise amplifier gain, 0-40 dB in 8 dB steps.</summary>
    void SetLnaGain(uint lnaGainDb);

    /// <summary>Baseband variable gain, 0-62 dB in 2 dB steps.</summary>
    void SetVgaGain(uint vgaGainDb);

    /// <summary>The 14 dB front-end amplifier. Helps weak signals but overloads easily on strong ones.</summary>
    void SetAmpEnabled(bool ampEnabled);

    Task<IqCapture> StartCapture(TimeSpan duration, CancellationToken cancellationToken = default);

    Task<IIqStream> StartStreaming(CancellationToken cancellationToken = default);
}
