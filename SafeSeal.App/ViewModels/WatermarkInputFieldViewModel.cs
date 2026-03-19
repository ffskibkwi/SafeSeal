using CommunityToolkit.Mvvm.ComponentModel;

namespace SafeSeal.App.ViewModels;

public partial class WatermarkInputFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string value;

    [ObservableProperty]
    private string label;

    public WatermarkInputFieldViewModel(int lineNumber, string initialValue)
    {
        label = $"Line {lineNumber}";
        value = initialValue;
    }
}