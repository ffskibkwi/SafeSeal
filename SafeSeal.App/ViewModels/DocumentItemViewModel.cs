using CommunityToolkit.Mvvm.ComponentModel;
using SafeSeal.Core;

namespace SafeSeal.App.ViewModels;

public partial class DocumentItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private DateTime updatedUtc;

    [ObservableProperty]
    private string originalExtension;

    [ObservableProperty]
    private bool isBatchSelected;

    public DocumentItemViewModel(Guid id, string displayName, DateTime updatedUtc, string originalExtension)
    {
        Id = id;
        this.displayName = displayName;
        this.updatedUtc = updatedUtc;
        this.originalExtension = originalExtension;
    }

    public Guid Id { get; }

    public string UpdatedDisplay => UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static DocumentItemViewModel FromEntry(DocumentEntry entry)
    {
        return new DocumentItemViewModel(entry.Id, entry.DisplayName, entry.UpdatedUtc, entry.OriginalExtension);
    }

    partial void OnUpdatedUtcChanged(DateTime value)
    {
        OnPropertyChanged(nameof(UpdatedDisplay));
    }
}
