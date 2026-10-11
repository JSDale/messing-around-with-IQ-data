namespace HackRfIqDataTest;

public interface ISoftwareDefineRadio
{
    void SetStartFrequency(double startFrequency);
    
    void SetEndFrequency(double endFrequency);

    void StartCapture();
}