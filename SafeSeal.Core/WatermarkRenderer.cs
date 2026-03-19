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

            int pixelWidth = baseImage.PixelWidth;
            int pixelHeight = baseImage.PixelHeight;
            double dpiX = baseImage.DpiX > 0 ? baseImage.DpiX : 96d;
            double dpiY = baseImage.DpiY > 0 ? baseImage.DpiY : 96d;

            double widthDip = baseImage.Width > 0 ? baseImage.Width : pixelWidth * 96d / dpiX;
            double heightDip = baseImage.Height > 0 ? baseImage.Height : pixelHeight * 96d / dpiY;

            IReadOnlyList<string> lines = NormalizeLines(options.TextLines);
            string watermarkText = string.Join(Environment.NewLine, lines);

            double opacity = Math.Clamp(options.Opacity, 0.05, 0.85);
            double fontSize = Math.Clamp(options.FontSize, 10d, 140d);
            double spacingX = Math.Clamp(options.HorizontalSpacing, 70d, 1200d);
            double spacingY = Math.Clamp(options.VerticalSpacing, 70d, 1200d);
            double angle = NormalizeAngle(options.AngleDegrees);
            double pixelsPerDip = Math.Clamp(dpiX / 96d, 0.8d, 4d);

            byte alpha = (byte)Math.Round(opacity * 255d);
            Color tint = options.TintColor;
            var brush = new SolidColorBrush(Color.FromArgb(alpha, tint.R, tint.G, tint.B));
            brush.Freeze();

            var drawingVisual = new DrawingVisual();
            using (DrawingContext context = drawingVisual.RenderOpen())
            {
                context.DrawImage(baseImage, new Rect(0, 0, widthDip, heightDip));

                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                var formattedText = new FormattedText(
                    watermarkText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    brush,
                    pixelsPerDip)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = Math.Max(120d, widthDip * 0.6d),
                };

                double textWidth = Math.Max(1d, formattedText.WidthIncludingTrailingWhitespace);
                double textHeight = Math.Max(fontSize, formattedText.Height);
                double diagonal = Math.Sqrt((widthDip * widthDip) + (heightDip * heightDip));
                double coverage = diagonal + Math.Max(textWidth, textHeight) * 2.2d;

                context.PushTransform(new TranslateTransform(widthDip / 2d, heightDip / 2d));
                context.PushTransform(new RotateTransform(angle));

                for (double y = -coverage; y <= coverage; y += spacingY)
                {
                    bool oddRow = Math.Abs(((int)Math.Floor((y + coverage) / spacingY)) % 2) == 1;
                    double rowOffset = oddRow ? spacingX / 2d : 0d;

                    for (double x = -coverage; x <= coverage; x += spacingX)
                    {
                        Point drawPoint = new(x + rowOffset - (textWidth / 2d), y - (textHeight / 2d));
                        context.DrawText(formattedText, drawPoint);
                    }
                }

                context.Pop();
                context.Pop();
            }

            var output = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
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

    private static IReadOnlyList<string> NormalizeLines(IReadOnlyList<string>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            return ["SAFESEAL"];
        }

        List<string> normalized = new(capacity: 5);
        foreach (string line in lines)
        {
            if (normalized.Count >= 5)
            {
                break;
            }

            string value = (line ?? string.Empty).Trim();
            normalized.Add(string.IsNullOrWhiteSpace(value) ? " " : value);
        }

        if (normalized.Count == 0)
        {
            return ["SAFESEAL"];
        }

        return normalized;
    }

    private static double NormalizeAngle(double angle)
    {
        double normalized = angle % 360d;
        if (normalized < 0)
        {
            normalized += 360d;
        }

        return normalized;
    }
}