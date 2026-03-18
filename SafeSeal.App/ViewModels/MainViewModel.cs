using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SafeSeal.App.Services;
using SafeSeal.Core;

namespace SafeSeal.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly UserFacingErrorHandler _errorHandler;
    private readonly WatermarkRenderer _watermarkRenderer;
    private readonly ExportService _exportService;
    private readonly SemaphoreSlim _previewLock = new(1, 1);

    [ObservableProperty]
    private VaultItemViewModel? selectedItem;

    [ObservableProperty]
    private BitmapSource? previewImage;

    [ObservableProperty]
    private string selectedTemplate;

    [ObservableProperty]
    private double opacity;

    [ObservableProperty]
    private int tileDensity;

    [ObservableProperty]
    private bool isBusy;

    public MainViewModel()
        : this(new UserFacingErrorHandler(), new WatermarkRenderer(), new ExportService())
    {
    }

    public MainViewModel(UserFacingErrorHandler errorHandler, WatermarkRenderer watermarkRenderer, ExportService exportService)
    {
        _errorHandler = errorHandler;
        _watermarkRenderer = watermarkRenderer;
        _exportService = exportService;

        VaultItems = new ObservableCollection<VaultItemViewModel>();
        TemplateOptions = new[]
        {
            "ONLY FOR {Purpose} - {Date}",
            "RESTRICTED USE BY {Recipient} ONLY",
            "FOR USE ONLY ON {Date}",
        };

        selectedTemplate = TemplateOptions[0];
        opacity = 0.20;
        tileDensity = 1;
    }

    public ObservableCollection<VaultItemViewModel> VaultItems { get; }

    public IReadOnlyList<string> TemplateOptions { get; }

    public bool IsLowDensity
    {
        get => TileDensity <= 0;
        set
        {
            if (value)
            {
                TileDensity = 0;
            }
        }
    }

    public bool IsMediumDensity
    {
        get => TileDensity == 1;
        set
        {
            if (value)
            {
                TileDensity = 1;
            }
        }
    }

    public bool IsHighDensity
    {
        get => TileDensity >= 2;
        set
        {
            if (value)
            {
                TileDensity = 2;
            }
        }
    }

    partial void OnSelectedItemChanged(VaultItemViewModel? value)
    {
        DeleteItemCommand.NotifyCanExecuteChanged();
        RenameItemCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();

        _ = RefreshPreviewAsync(value);
    }

    partial void OnPreviewImageChanged(BitmapSource? value)
    {
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        AddItemCommand.NotifyCanExecuteChanged();
        DeleteItemCommand.NotifyCanExecuteChanged();
        RenameItemCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTemplateChanged(string value)
    {
        _ = RefreshPreviewAsync(SelectedItem);
    }

    partial void OnOpacityChanged(double value)
    {
        _ = RefreshPreviewAsync(SelectedItem);
    }

    partial void OnTileDensityChanged(int value)
    {
        OnPropertyChanged(nameof(IsLowDensity));
        OnPropertyChanged(nameof(IsMediumDensity));
        OnPropertyChanged(nameof(IsHighDensity));
        _ = RefreshPreviewAsync(SelectedItem);
    }

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private async Task AddItemAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SafeSeal Vault (*.seal)|*.seal",
            Multiselect = false,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string filePath = dialog.FileName;

        try
        {
            IsBusy = true;

            var info = new FileInfo(filePath);
            var item = new VaultItemViewModel(
                info.Name,
                info.Exists ? info.CreationTimeLocal : DateTime.Now,
                info.Exists ? info.Length : 0,
                filePath);

            VaultItems.Add(item);
            SelectedItem = item;

            await RefreshPreviewAsync(item);
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedItem))]
    private void DeleteItem()
    {
        if (SelectedItem is null)
        {
            return;
        }

        VaultItems.Remove(SelectedItem);
        SelectedItem = VaultItems.Count > 0 ? VaultItems[0] : null;
        if (SelectedItem is null)
        {
            PreviewImage = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedItem))]
    private async Task RenameItemAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        try
        {
            IsBusy = true;

            string currentPath = SelectedItem.FilePath;
            string directory = Path.GetDirectoryName(currentPath) ?? throw new InvalidOperationException("Selected vault path is invalid.");
            string baseName = Path.GetFileNameWithoutExtension(currentPath);
            string extension = Path.GetExtension(currentPath);

            string candidateName = $"{baseName}_renamed{extension}";
            string targetPath = Path.Combine(directory, candidateName);
            int counter = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(directory, $"{baseName}_renamed_{counter}{extension}");
                counter++;
            }

            await Task.Run(() => File.Move(currentPath, targetPath));

            SelectedItem.FilePath = targetPath;
            SelectedItem.Name = Path.GetFileName(targetPath);
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        string originalName = Path.GetFileNameWithoutExtension(SelectedItem.Name);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm");

        var dialog = new SaveFileDialog
        {
            Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png",
            FileName = $"SafeSeal_Export_{originalName}_{timestamp}",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBusy = true;

            BitmapSource renderedPreview = await BuildPreviewAsync(SelectedItem);
            PreviewImage = renderedPreview;

            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (extension == ".png")
            {
                await Task.Run(() => _exportService.ExportAsPng(renderedPreview, dialog.FileName));
            }
            else
            {
                await Task.Run(() => _exportService.ExportAsJpeg(renderedPreview, dialog.FileName, quality: 85));
            }
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddItem() => !IsBusy;

    private bool CanModifySelectedItem() => SelectedItem is not null && !IsBusy;

    private bool CanExport() => SelectedItem is not null && PreviewImage is not null && !IsBusy;

    private async Task RefreshPreviewAsync(VaultItemViewModel? item)
    {
        if (item is null)
        {
            PreviewImage = null;
            return;
        }

        try
        {
            await _previewLock.WaitAsync();
            IsBusy = true;

            BitmapSource rendered = await BuildPreviewAsync(item);
            PreviewImage = rendered;
        }
        catch (Exception ex)
        {
            PreviewImage = null;
            _errorHandler.Show(ex);
        }
        finally
        {
            IsBusy = false;
            _previewLock.Release();
        }
    }

    private Task<BitmapSource> BuildPreviewAsync(VaultItemViewModel item)
    {
        WatermarkOptions options = new(SelectedTemplate, Opacity, TileDensity);

        return Task.Run(() =>
        {
            byte[] imageBytes = VaultManager.LoadSecurely(item.FilePath);
            return _watermarkRenderer.Render(imageBytes, options);
        });
    }
}
