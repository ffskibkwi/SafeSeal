using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SafeSeal.Core;

public sealed class WatermarkRenderer
{
    public BitmapSource Render(byte[] imageBytes, WatermarkOptions options)
    {
        if (imageBytes is null)
        {
            throw new ArgumentException("Image bytes cannot be null.", nameof(imageBytes));
        }

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("Image bytes cannot be empty.", nameof(imageBytes));
        }

        if (options is null)
        {
            throw new ArgumentException("Watermark options cannot be null.", nameof(options));
        }

        GCHandle imageHandle = default;
        bool isPinned = false;

        try
        {
            imageHandle = GCHandle.Alloc(imageBytes, GCHandleType.Pinned);
            isPinned = true;

            using var stream = new MemoryStream(imageBytes, writable: false);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource baseImage = decoder.Frames[0];

            int width = baseImage.PixelWidth;
            int height = baseImage.PixelHeight;
            double dpiX = baseImage.DpiX > 0 ? baseImage.DpiX : 96;
            double dpiY = baseImage.DpiY > 0 ? baseImage.DpiY : 96;

            string template = string.IsNullOrWhiteSpace(options.Template) ? "ONLY FOR {Date}" : options.Template;
            string watermarkText = ExpandTemplate(template);
            double opacity = Math.Clamp(options.Opacity, 0.10, 0.40);
            int spacing = ResolveSpacing(options.TileDensity);

            var drawingVisual = new DrawingVisual();
            using (DrawingContext context = drawingVisual.RenderOpen())
            {
                context.DrawImage(baseImage, new Rect(0, 0, width, height));

                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                var formattedText = new FormattedText(
                    watermarkText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    28,
                    Brushes.Red,
                    1.0);

                context.PushOpacity(opacity);
                context.PushTransform(new RotateTransform(-35, width / 2d, height / 2d));

                for (int y = -height; y <= (height * 2); y += spacing)
                {
                    for (int x = -width; x <= (width * 2); x += spacing)
                    {
                        context.DrawText(formattedText, new Point(x, y));
                    }
                }

                context.Pop();
                context.Pop();
            }

            var output = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
            output.Render(drawingVisual);
            output.Freeze();
            return output;
        }
        finally
        {
            if (isPinned)
            {
                imageHandle.Free();
            }

            Array.Clear(imageBytes, 0, imageBytes.Length);
        }
    }

    private static int ResolveSpacing(int tileDensity)
    {
        return tileDensity switch
        {
            <= 0 => 450,
            1 => 350,
            _ => 250,
        };
    }

    private static string ExpandTemplate(string template)
    {
        string expanded = template.Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        expanded = expanded.Replace("{Machine}", string.Empty, StringComparison.Ordinal);
        return expanded.Trim();
    }
}
