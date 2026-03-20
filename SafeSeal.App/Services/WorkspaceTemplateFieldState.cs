using CommunityToolkit.Mvvm.ComponentModel;

namespace SafeSeal.App.Services;

public sealed partial class WorkspaceTemplateFieldState : ObservableObject
{
    [ObservableProperty]
    private string label;

    [ObservableProperty]
    private string value;

    public WorkspaceTemplateFieldState(string key, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Template field key cannot be empty.", nameof(key));
        }

        Key = key.Trim();
        this.label = label ?? string.Empty;
        this.value = value ?? string.Empty;
    }

    public string Key { get; }
}
