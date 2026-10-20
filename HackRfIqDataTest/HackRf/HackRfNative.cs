using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HackRfIqDataTest.HackRf;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HackRfTransfer
{
    public nint Device;
    public byte* Buffer;
    public int BufferLength;
    public int ValidLength;
    public void* RxContext;
    public void* TxContext;
}

/// <summary>
/// P/Invoke bindings for libhackrf (Ubuntu: <c>sudo apt install libhackrf0 hackrf</c>).
/// </summary>
internal static unsafe partial class HackRfNative
{
    private const string LibraryName = "hackrf";

    public const int Success = 0;

    static HackRfNative()
    {
        // The runtime package only ships libhackrf.so.0; libhackrf.so comes with the -dev package.
        NativeLibrary.SetDllImportResolver(typeof(HackRfNative).Assembly, ResolveLibrary);
    }

    [LibraryImport(LibraryName, EntryPoint = "hackrf_init")]
    public static partial int Init();

    [LibraryImport(LibraryName, EntryPoint = "hackrf_exit")]
    public static partial int Exit();

    [LibraryImport(LibraryName, EntryPoint = "hackrf_open")]
    public static partial int Open(out nint device);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_close")]
    public static partial int Close(nint device);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_set_freq")]
    public static partial int SetFrequency(nint device, ulong frequencyHz);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_set_sample_rate")]
    public static partial int SetSampleRate(nint device, double sampleRateHz);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_compute_baseband_filter_bw")]
    public static partial uint ComputeBasebandFilterBandwidth(uint bandwidthHz);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_set_baseband_filter_bandwidth")]
    public static partial int SetBasebandFilterBandwidth(nint device, uint bandwidthHz);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_set_lna_gain")]
    public static partial int SetLnaGain(nint device, uint gainDb);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_set_vga_gain")]
    public static partial int SetVgaGain(nint device, uint gainDb);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_set_amp_enable")]
    public static partial int SetAmpEnable(nint device, byte enabled);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_start_rx")]
    public static partial int StartRx(nint device, delegate* unmanaged[Cdecl]<HackRfTransfer*, int> callback, void* rxContext);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_stop_rx")]
    public static partial int StopRx(nint device);

    [LibraryImport(LibraryName, EntryPoint = "hackrf_error_name")]
    private static partial nint ErrorName(int error);

    public static void ThrowIfError(int result, string operation)
    {
        if (result != Success)
        {
            var name = Marshal.PtrToStringUTF8(ErrorName(result)) ?? "unknown error";
            throw new HackRfException($"{operation} failed: {name} ({result})");
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return 0;
        }

        foreach (var candidate in new[] { "libhackrf.so.0", "libhackrf.so", "libhackrf.dylib", "hackrf.dll" })
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
            {
                return handle;
            }
        }

        return 0;
    }
}

public sealed class HackRfException(string message) : Exception(message);
