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

    public static WatermarkInputFieldViewModel CreateLine(int lineNumber, string initialValue)
    {
        return new WatermarkInputFieldViewModel($"Line {lineNumber}", initialValue);
    }
}
