using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MinimumEffortClicker.App;

internal static class Imaging
{
    /// <summary>Copies tightly packed BGRA pixels into a bitmap Avalonia can draw, save as PNG or copy.</summary>
    public static WriteableBitmap FromBgra(byte[] pixels, int width, int height, bool opaque = true)
    {
        if (width < 1 || height < 1 || pixels.Length < (long)width * height * 4) throw new ArgumentException("The image is empty or truncated.");
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, opaque ? AlphaFormat.Opaque : AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        for (int row = 0; row < height; row++)
            Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
        return bitmap;
    }

    /// <summary>One rectangle of a BGRA buffer.</summary>
    public static byte[] Crop(byte[] pixels, int width, int x, int y, int cropWidth, int cropHeight)
    {
        var result = new byte[checked(cropWidth * cropHeight * 4)];
        for (int row = 0; row < cropHeight; row++)
            Array.Copy(pixels, ((y + row) * width + x) * 4, result, row * cropWidth * 4, cropWidth * 4);
        return result;
    }
}
