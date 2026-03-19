using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SafeSeal.App.Dialogs;
using SafeSeal.App.Services;
using SafeSeal.Core;

namespace SafeSeal.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDocumentVaultService _documentVaultService;
    private readonly UserFacingErrorHandler _errorHandler;
    private readonly SemaphoreSlim _previewLock = new(1, 1);
    private CancellationTokenSource? _previewCts;
    private int _previewRequestId;

    [ObservableProperty]
    private DocumentItemViewModel? selectedDocument;

    [ObservableProperty]
    private BitmapSource? previewImage;

    [ObservableProperty]
    private bool isDetailPaneOpen;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private WatermarkTintOption? selectedTintOption;

    [ObservableProperty]
    private int selectedLineCount;

    [ObservableProperty]
    private double opacity;

    [ObservableProperty]
    private double fontSize;

    [ObservableProperty]
    private double horizontalSpacing;

    [ObservableProperty]
    private double verticalSpacing;

    [ObservableProperty]
    private double angleDegrees;

    [ObservableProperty]
    private string statusMessage;

    public MainViewModel()
        : this(new DocumentVaultService(), new UserFacingErrorHandler())
    {
    }

    public MainViewModel(IDocumentVaultService documentVaultService, UserFacingErrorHandler errorHandler)
    {
        _documentVaultService = documentVaultService;
        _errorHandler = errorHandler;

        Documents = new ObservableCollection<DocumentItemViewModel>();
        WatermarkLines = new ObservableCollection<WatermarkInputFieldViewModel>();

        LineCountOptions = [1, 2, 3, 4, 5];
        TintOptions =
        [
            new WatermarkTintOption("Blue", Color.FromRgb(0x25, 0x63, 0xEB)),
            new WatermarkTintOption("Slate", Color.FromRgb(0x33, 0x48, 0x55)),
            new WatermarkTintOption("Crimson", Color.FromRgb(0xB9, 0x1C, 0x1C)),
            new WatermarkTintOption("Forest", Color.FromRgb(0x16, 0x6A, 0x53)),
        ];

        selectedTintOption = TintOptions[0];
        selectedLineCount = 1;
        opacity = 0.22;
        fontSize = 28;
        horizontalSpacing = 330;
        verticalSpacing = 250;
        angleDegrees = 35;
        statusMessage = string.Empty;

        RebuildLineInputs(selectedLineCount, refreshPreview: false);
        _ = InitializeAsync();
    }

    public ObservableCollection<DocumentItemViewModel> Documents { get; }

    public ObservableCollection<WatermarkInputFieldViewModel> WatermarkLines { get; }

    public IReadOnlyList<int> LineCountOptions { get; }

    public IReadOnlyList<WatermarkTintOption> TintOptions { get; }

    public bool IsPreviewEmpty => PreviewImage is null;

    public bool IsEmptyStateVisible => Documents.Count == 0;

    partial void OnSelectedDocumentChanged(DocumentItemViewModel? value)
    {
        IsDetailPaneOpen = value is not null;
        _ = RefreshPreviewAsync(value);
    }

    partial void OnPreviewImageChanged(BitmapSource? value)
    {
        OnPropertyChanged(nameof(IsPreviewEmpty));
    }

    partial void OnSelectedTintOptionChanged(WatermarkTintOption? value)
    {
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnSelectedLineCountChanged(int value)
    {
        RebuildLineInputs(value, refreshPreview: true);
    }

    partial void OnOpacityChanged(double value)
    {
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnFontSizeChanged(double value)
    {
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnHorizontalSpacingChanged(double value)
    {
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnVerticalSpacingChanged(double value)
    {
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnAngleDegreesChanged(double value)
    {
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff",
            Multiselect = false,
            CheckFileExists = true,
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        string initialName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
        NicknameDialog nicknameDialog = new("Import Image", "Image Name", initialName)
        {
            Owner = Application.Current.MainWindow,
        };

        if (nicknameDialog.ShowDialog() != true)
        {
            return;
        }

        string displayName = nicknameDialog.Nickname;
        DocumentItemViewModel? existing = FindByDisplayName(displayName);

        if (existing is not null)
        {
            MessageBoxResult overwriteResult = MessageBox.Show(
                "A document with this name already exists. Overwrite it?",
                "SafeSeal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (overwriteResult != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Importing document...";

            DocumentEntry imported = await _documentVaultService.ImportAsync(
                openFileDialog.FileName,
                displayName,
                NameConflictBehavior.AskOverwrite,
                CancellationToken.None);

            await ReloadDocumentsAsync(imported.Id);
            StatusMessage = "Document imported into secure vault.";
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

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedDocument is null)
        {
            return;
        }

        string baseName = SelectedDocument.DisplayName.Replace(' ', '_');
        string defaultName = $"SafeSeal_Export_{baseName}_{DateTime.Now:yyyyMMdd_HHmm}";

        var saveDialog = new SaveFileDialog
        {
            Filter = "JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png",
            FileName = defaultName,
            AddExtension = true,
        };

        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Exporting image...";

            await _documentVaultService.ExportAsync(
                SelectedDocument.Id,
                BuildWatermarkOptions(),
                saveDialog.FileName,
                85,
                CancellationToken.None);

            StatusMessage = "Export completed.";
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

    [RelayCommand]
    private async Task RenameAsync(DocumentItemViewModel? item)
    {
        DocumentItemViewModel? target = item ?? SelectedDocument;
        if (target is null)
        {
            return;
        }

        NicknameDialog renameDialog = new("Rename Document", "Image Name", target.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };

        if (renameDialog.ShowDialog() != true)
        {
            return;
        }

        string newName = renameDialog.Nickname;
        if (string.Equals(newName, target.DisplayName, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Renaming document...";

            await _documentVaultService.RenameAsync(target.Id, newName, CancellationToken.None);
            await ReloadDocumentsAsync(target.Id);

            StatusMessage = "Document renamed.";
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

    [RelayCommand]
    private async Task DeleteAsync(DocumentItemViewModel? item)
    {
        DocumentItemViewModel? target = item ?? SelectedDocument;
        if (target is null)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Delete '{target.DisplayName}' from secure vault?",
            "SafeSeal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Deleting document...";

            await _documentVaultService.DeleteAsync(target.Id, CancellationToken.None);
            await ReloadDocumentsAsync(Guid.Empty);

            if (SelectedDocument?.Id == target.Id)
            {
                SelectedDocument = null;
                PreviewImage = null;
            }

            StatusMessage = "Document deleted.";
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

    [RelayCommand]
    private void CloseDetail()
    {
        _previewCts?.Cancel();
        IsDetailPaneOpen = false;
        SelectedDocument = null;
        PreviewImage = null;
        StatusMessage = string.Empty;
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Loading vault...";

            await ReloadDocumentsAsync(Guid.Empty);

            StatusMessage = Documents.Count == 0 ? "Import a photo to get started." : "Vault loaded.";
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

    private async Task ReloadDocumentsAsync(Guid preferredSelection)
    {
        IReadOnlyList<DocumentEntry> entries = await _documentVaultService.ListAsync(CancellationToken.None);

        Documents.Clear();
        foreach (DocumentEntry entry in entries)
        {
            Documents.Add(DocumentItemViewModel.FromEntry(entry));
        }

        OnPropertyChanged(nameof(IsEmptyStateVisible));

        if (Documents.Count == 0)
        {
            SelectedDocument = null;
            return;
        }

        DocumentItemViewModel? preferred = null;
        if (preferredSelection != Guid.Empty)
        {
            foreach (DocumentItemViewModel doc in Documents)
            {
                if (doc.Id == preferredSelection)
                {
                    preferred = doc;
                    break;
                }
            }
        }

        SelectedDocument = preferred ?? Documents[0];
    }

    private DocumentItemViewModel? FindByDisplayName(string name)
    {
        foreach (DocumentItemViewModel item in Documents)
        {
            if (string.Equals(item.DisplayName, name, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private void RebuildLineInputs(int count, bool refreshPreview)
    {
        int normalizedCount = Math.Clamp(count, 1, 5);

        List<string> existingValues = new(capacity: WatermarkLines.Count);
        foreach (WatermarkInputFieldViewModel line in WatermarkLines)
        {
            line.PropertyChanged -= OnLineInputPropertyChanged;
            existingValues.Add(line.Value ?? string.Empty);
        }

        WatermarkLines.Clear();

        for (int index = 0; index < normalizedCount; index++)
        {
            string initial = index < existingValues.Count ? existingValues[index] : index == 0 ? "FOR INTERNAL USE" : string.Empty;
            var line = new WatermarkInputFieldViewModel(index + 1, initial);
            line.PropertyChanged += OnLineInputPropertyChanged;
            WatermarkLines.Add(line);
        }

        if (SelectedLineCount != normalizedCount)
        {
            SelectedLineCount = normalizedCount;
        }

        if (refreshPreview)
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }

    private void OnLineInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WatermarkInputFieldViewModel.Value))
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }

    private async Task RefreshPreviewAsync(DocumentItemViewModel? item)
    {
        int requestId = Interlocked.Increment(ref _previewRequestId);
        _previewCts?.Cancel();

        if (item is null || !IsDetailPaneOpen)
        {
            PreviewImage = null;
            return;
        }

        CancellationTokenSource localCts = new();
        _previewCts = localCts;

        bool lockTaken = false;
        try
        {
            await _previewLock.WaitAsync(localCts.Token);
            lockTaken = true;

            if (requestId != _previewRequestId || localCts.IsCancellationRequested)
            {
                return;
            }

            BitmapSource image = await _documentVaultService.BuildPreviewAsync(item.Id, BuildWatermarkOptions(), localCts.Token);

            if (!localCts.IsCancellationRequested
                && requestId == _previewRequestId
                && IsDetailPaneOpen
                && SelectedDocument?.Id == item.Id)
            {
                PreviewImage = image;
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore stale preview requests.
        }
        catch (Exception ex)
        {
            PreviewImage = null;
            _errorHandler.Show(ex);
        }
        finally
        {
            localCts.Dispose();
            if (ReferenceEquals(_previewCts, localCts))
            {
                _previewCts = null;
            }

            if (lockTaken)
            {
                _previewLock.Release();
            }
        }
    }

    private WatermarkOptions BuildWatermarkOptions()
    {
        List<string> lines = new(capacity: WatermarkLines.Count);
        foreach (WatermarkInputFieldViewModel line in WatermarkLines)
        {
            lines.Add(line.Value?.Trim() ?? string.Empty);
        }

        return new WatermarkOptions(
            lines,
            Opacity,
            FontSize,
            HorizontalSpacing,
            VerticalSpacing,
            AngleDegrees,
            SelectedTintOption?.Color ?? Color.FromRgb(0x25, 0x63, 0xEB));
    }
}