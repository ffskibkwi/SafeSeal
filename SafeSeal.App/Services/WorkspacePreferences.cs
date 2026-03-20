using System.Collections.ObjectModel;

namespace SafeSeal.App.Services;

public sealed record WorkspacePreferences
{
    public string TemplateId { get; init; } = "custom-multi-line";

    public int SelectedLineCount { get; init; } = 1;

    public Dictionary<string, string> TemplateFieldValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> CustomLines { get; init; } = ["FOR INTERNAL USE"];

    public string TintKey { get; init; } = "blue";

    public double Opacity { get; init; } = 0.22;

    public double FontSize { get; init; } = 28;

    public double HorizontalSpacing { get; init; } = 330;

    public double VerticalSpacing { get; init; } = 250;

    public double AngleDegrees { get; init; } = 35;

    public string ValidityMode { get; init; } = "None";

    public string DatePreset { get; init; } = "Today";

    public string ExpiryPreset { get; init; } = "Today";

    public DateTime? CustomDate { get; init; } = DateTime.Today;

    public DateTime? CustomExpiryDate { get; init; } = DateTime.Today;

    public static WorkspacePreferences CreateDefault()
    {
        return new WorkspacePreferences();
    }

    public WorkspacePreferences DeepCopy()
    {
        return this with
        {
            TemplateFieldValues = new Dictionary<string, string>(TemplateFieldValues, StringComparer.OrdinalIgnoreCase),
            CustomLines = [..CustomLines],
        };
    }
}
