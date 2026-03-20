using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SafeSeal.App.ViewModels;

public partial class WatermarkInputFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string value;

    [ObservableProperty]
    private string label;

    public WatermarkInputFieldViewModel(string label, string initialValue)
    {
        this.label = label;
        value = initialValue;
    }

    public static WatermarkInputFieldViewModel CreateLine(int lineNumber, string initialValue, string labelFormat)
    {
        string resolvedLabel = string.IsNullOrWhiteSpace(labelFormat)
            ? string.Format(CultureInfo.CurrentCulture, "Line {0}", lineNumber)
            : string.Format(CultureInfo.CurrentCulture, labelFormat, lineNumber);

        return new WatermarkInputFieldViewModel(resolvedLabel, initialValue);
    }
}
