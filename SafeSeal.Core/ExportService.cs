using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace SafeSeal.Core;

public sealed class ExportService
{
    public void ExportAsJpeg(BitmapSource image, string outputPath, int quality)
    {
        ValidateInputs(image, outputPath);

        int clampedQuality = Math.Clamp(quality, 70, 95);
        if (clampedQuality != quality)
        {
            Trace.TraceWarning("JPEG quality {0} is outside 70-95 and was clamped to {1}.", quality, clampedQuality);
        }

        var encoder = new JpegBitmapEncoder
        {
            QualityLevel = clampedQuality,
        };

        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);

        ForceBitmapCleanup();
    }

    public void ExportAsPng(BitmapSource image, string outputPath)
    {
        ValidateInputs(image, outputPath);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);

        ForceBitmapCleanup();
    }

    private static void ValidateInputs(BitmapSource image, string outputPath)
    {
        if (image is null)
        {
            throw new ArgumentException("Image cannot be null.", nameof(image));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(outputPath));
        }
    }

    private static void ForceBitmapCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
