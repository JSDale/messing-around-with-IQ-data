using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HackRfIqDataTest.Analysis;

public static class WaterfallRenderer
{
    // Colour stops from weak to strong signal: black, navy, blue, green, yellow, red.
    private static readonly (double Position, byte R, byte G, byte B)[] Palette =
    [
        (0.0, 0, 0, 0),
        (0.2, 0, 0, 128),
        (0.4, 0, 128, 255),
        (0.6, 0, 255, 128),
        (0.8, 255, 255, 0),
        (1.0, 255, 0, 0),
    ];

    public static unsafe WriteableBitmap Render(WaterfallData data)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(data.Width, data.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        var range = Math.Max(data.PeakDb - data.FloorDb, 1e-6);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < data.Height; y++)
        {
            var row = (uint*)(buffer.Address + y * buffer.RowBytes);
            for (var x = 0; x < data.Width; x++)
            {
                var level = (data.PowerDb[y * data.Width + x] - data.FloorDb) / range;
                row[x] = ToBgra(Math.Clamp(level, 0, 1));
            }
        }

        return bitmap;
    }

    private static uint ToBgra(double level)
    {
        var upper = 1;
        while (upper < Palette.Length - 1 && Palette[upper].Position < level)
        {
            upper++;
        }

        var (p0, r0, g0, b0) = Palette[upper - 1];
        var (p1, r1, g1, b1) = Palette[upper];
        var t = (level - p0) / (p1 - p0);
        var r = (uint)(r0 + (r1 - r0) * t);
        var g = (uint)(g0 + (g1 - g0) * t);
        var b = (uint)(b0 + (b1 - b0) * t);
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }
}
