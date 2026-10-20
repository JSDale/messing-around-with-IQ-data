using System;
using System.Threading;
using System.Threading.Tasks;
using HackRfIqDataTest.Models;

namespace HackRfIqDataTest;

public interface ISoftwareDefineRadio
{
    void SetStartFrequency(double startFrequencyHz);
    
    void SetEndFrequency(double endFrequencyHz);

    Task<IqCapture> StartCapture(TimeSpan duration, CancellationToken cancellationToken = default);

    Task<IIqStream> StartStreaming(CancellationToken cancellationToken = default);
}
