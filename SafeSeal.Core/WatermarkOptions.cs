using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SafeSeal.Core;

public sealed record WatermarkOptions(
    IReadOnlyList<string> TextLines,
    double Opacity,
    double FontSize,
    double HorizontalSpacing,
    double VerticalSpacing,
    double AngleDegrees,
    Color TintColor,
    string TemplateId = "custom-multi-line",
    int TemplateVersion = 1,
    string? SignatureId = null)
{
    public WatermarkOptions(string template, double opacity, int tileDensity)
        : this(
            BuildLines(template, null),
            opacity,
            28d,
            ResolveSpacing(tileDensity),
            ResolveSpacing(tileDensity),
            35d,
            Color.FromRgb(0x25, 0x63, 0xEB),
            "legacy-free-text",
            1,
            null)
    {
    }

    public WatermarkOptions(
        string template,
        double opacity,
        int tileDensity,
        double fontSize,
        double horizontalSpacing,
        double verticalSpacing,
        Color tintColor,
        IReadOnlyDictionary<string, string>? templateValues)
        : this(
            BuildLines(template, templateValues),
            opacity,
            fontSize,
            horizontalSpacing,
            verticalSpacing,
            35d,
            tintColor,
            "legacy-free-text",
            1,
            null)
    {
    }

    public WatermarkOptions WithSignature(string signatureId)
    {
        if (string.IsNullOrWhiteSpace(signatureId))
        {
            return this with { SignatureId = null };
        }

        return this with { SignatureId = signatureId.Trim() };
    }

    private static IReadOnlyList<string> BuildLines(string template, IReadOnlyDictionary<string, string>? values)
    {
        string text = string.IsNullOrWhiteSpace(template) ? "SAFESEAL" : template;
        text = text.Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        if (values is not null)
        {
            foreach ((string key, string value) in values)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                text = text.Replace($"{{{key.Trim()}}}", value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        text = Regex.Replace(text, "\\{[A-Za-z0-9_]+\\}", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ["SAFESEAL"];
        }

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return ["SAFESEAL"];
        }

        return lines.Take(5).ToArray();
    }

    private static double ResolveSpacing(int tileDensity)
    {
        return tileDensity switch
        {
            <= 0 => 450d,
            1 => 350d,
            _ => 250d,
        };
    }
}
