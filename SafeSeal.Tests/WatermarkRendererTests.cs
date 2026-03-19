using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class WatermarkRendererTests
{
    [Fact]
    public void Render_PreservesSourceDimensionsAndQuadrants_ForHighDpiInput()
    {
        const int width = 200;
        const int height = 120;
        byte[] png = CreateQuadrantPng(width, height, 300, 300);

        var renderer = new WatermarkRenderer();
        var options = new WatermarkOptions(
            ["SAFE"],
            0.05,
            10,
            2000,
            2000,
            35,
            Colors.White);

        BitmapSource output = renderer.Render(png, options);

        Assert.Equal(width, output.PixelWidth);
        Assert.Equal(height, output.PixelHeight);

        Color tl = ReadPixel(output, width / 4, height / 4);
        Color tr = ReadPixel(output, (width * 3) / 4, height / 4);
        Color bl = ReadPixel(output, width / 4, (height * 3) / 4);
        Color br = ReadPixel(output, (width * 3) / 4, (height * 3) / 4);

        Assert.True(IsNear(tl, Colors.Red), "Top-left quadrant color was not preserved.");
        Assert.True(IsNear(tr, Colors.Green), "Top-right quadrant color was not preserved.");
        Assert.True(IsNear(bl, Colors.Blue), "Bottom-left quadrant color was not preserved.");
        Assert.True(IsNear(br, Colors.Yellow), "Bottom-right quadrant color was not preserved.");
    }

    [Fact]
    public void Render_TilesMultilineWatermarkAcrossWholeSurface_AtCustomAngle()
    {
        const int width = 480;
        const int height = 320;
        byte[] png = CreateSolidPng(width, height, 96, 96, Colors.White);

        var renderer = new WatermarkRenderer();
        var options = new WatermarkOptions(
            ["AUDIT ONLY", "DO NOT SHARE"],
            0.75,
            34,
            120,
            100,
            120,
            Color.FromRgb(0x10, 0x4E, 0xC5));

        BitmapSource output = renderer.Render(png, options);

        int changed = 0;
        int topHits = 0;
        int bottomHits = 0;
        int leftHits = 0;
        int rightHits = 0;

        int stride = output.PixelWidth * 4;
        byte[] pixels = new byte[stride * output.PixelHeight];
        output.CopyPixels(pixels, stride, 0);

        for (int y = 0; y < output.PixelHeight; y++)
        {
            for (int x = 0; x < output.PixelWidth; x++)
            {
                int index = (y * stride) + (x * 4);
                byte b = pixels[index];
                byte g = pixels[index + 1];
                byte r = pixels[index + 2];

                bool isChanged = r < 245 || g < 245 || b < 245;
                if (!isChanged)
                {
                    continue;
                }

                changed++;
                if (x < output.PixelWidth / 2)
                {
                    leftHits++;
                }
                else
                {
                    rightHits++;
                }

                if (y < output.PixelHeight / 2)
                {
                    topHits++;
                }
                else
                {
                    bottomHits++;
                }
            }
        }

        int totalPixels = output.PixelWidth * output.PixelHeight;
        double changedRatio = (double)changed / totalPixels;

        Assert.True(changedRatio > 0.012, $"Expected broader tiling coverage, got ratio {changedRatio:F4}.");
        Assert.True(leftHits > 0 && rightHits > 0, "Expected watermark to appear on both left and right halves.");
        Assert.True(topHits > 0 && bottomHits > 0, "Expected watermark to appear on both top and bottom halves.");
    }

    private static byte[] CreateQuadrantPng(int width, int height, double dpiX, double dpiY)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = (x < width / 2, y < height / 2) switch
                {
                    (true, true) => Colors.Red,
                    (false, true) => Colors.Green,
                    (true, false) => Colors.Blue,
                    _ => Colors.Yellow,
                };

                int index = (y * stride) + (x * 4);
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSolidPng(int width, int height, double dpiX, double dpiY, Color color)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Color ReadPixel(BitmapSource source, int x, int y)
    {
        byte[] pixel = new byte[4];
        source.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static bool IsNear(Color actual, Color expected)
    {
        const int tolerance = 35;
        return Math.Abs(actual.R - expected.R) <= tolerance
            && Math.Abs(actual.G - expected.G) <= tolerance
            && Math.Abs(actual.B - expected.B) <= tolerance;
    }
}