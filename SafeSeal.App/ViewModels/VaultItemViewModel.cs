using CommunityToolkit.Mvvm.ComponentModel;

namespace SafeSeal.App.ViewModels;

public partial class VaultItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private DateTime dateAdded;

    [ObservableProperty]
    private long fileSizeBytes;

    [ObservableProperty]
    private string filePath;

    public VaultItemViewModel(string name, DateTime dateAdded, long fileSizeBytes, string filePath)
    {
        this.name = name;
        this.dateAdded = dateAdded;
        this.fileSizeBytes = fileSizeBytes;
        this.filePath = filePath;
    }

    public string FileSize => FileSizeBytes < 1024
        ? $"{FileSizeBytes} B"
        : $"{(FileSizeBytes / 1024d):F1} KB";

    partial void OnFileSizeBytesChanged(long value)
    {
        OnPropertyChanged(nameof(FileSize));
    }
}
