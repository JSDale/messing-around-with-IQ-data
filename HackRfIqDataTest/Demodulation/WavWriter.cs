using System;
using System.IO;
using System.Text;

namespace HackRfIqDataTest.Demodulation;

/// <summary>
/// Writes mono 16-bit PCM WAV: a 44-byte RIFF header followed by the samples.
/// </summary>
public sealed class WavWriter : IDisposable
{
    private const int HeaderBytes = 44;

    private readonly BinaryWriter _writer;
    private long _dataBytes;

    public WavWriter(string path, int sampleRate)
    {
        this._writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write), Encoding.ASCII);

        // The two sizes are not known yet; they are filled in on Dispose.
        this._writer.Write("RIFF"u8);
        this._writer.Write(0u);
        this._writer.Write("WAVE"u8);
        this._writer.Write("fmt "u8);
        this._writer.Write(16u);                    // fmt chunk size
        this._writer.Write((ushort)1);              // PCM
        this._writer.Write((ushort)1);              // channels
        this._writer.Write((uint)sampleRate);
        this._writer.Write((uint)sampleRate * 2);   // bytes per second
        this._writer.Write((ushort)2);              // bytes per sample frame
        this._writer.Write((ushort)16);             // bits per sample
        this._writer.Write("data"u8);
        this._writer.Write(0u);
    }

    /// <param name="samples">Audio in the range ±1; anything outside is clipped.</param>
    public void Write(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            this._writer.Write((short)Math.Clamp(sample * 32767f, short.MinValue, short.MaxValue));
        }

        this._dataBytes += samples.Length * 2L;
    }

    public void Dispose()
    {
        this._writer.Seek(4, SeekOrigin.Begin);
        this._writer.Write((uint)(HeaderBytes - 8 + this._dataBytes));
        this._writer.Seek(HeaderBytes - 4, SeekOrigin.Begin);
        this._writer.Write((uint)this._dataBytes);
        this._writer.Dispose();
    }
}
