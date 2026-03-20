using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
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
    private readonly LocalizationService _localization;
    private readonly ISafeTransferService _safeTransferService = new SafeTransferService();
    private readonly ITransferPackageService _transferPackageService = new TransferPackageService();
    private readonly IBatchWatermarkService _batchWatermarkService = new BatchWatermarkService();
    private readonly HiddenVaultStorageService _hiddenStorageService = new(SafeSealStorageOptions.CreateDefault());
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
    private WatermarkTemplateDefinition? selectedTemplate;

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
        _localization = LocalizationService.Instance;
        _localization.LanguageChanged += OnLanguageChanged;

        Documents = new ObservableCollection<DocumentItemViewModel>();
        WatermarkLines = new ObservableCollection<WatermarkInputFieldViewModel>();
        TemplateFields = new ObservableCollection<WatermarkInputFieldViewModel>();
        Templates = new ObservableCollection<WatermarkTemplateDefinition>();

        LineCountOptions = [1, 2, 3, 4, 5];
        TintOptions =
        [
            new WatermarkTintOption(_localization["TintBlue"], Color.FromRgb(0x25, 0x63, 0xEB)),
            new WatermarkTintOption(_localization["TintSlate"], Color.FromRgb(0x33, 0x48, 0x55)),
            new WatermarkTintOption(_localization["TintCrimson"], Color.FromRgb(0xB9, 0x1C, 0x1C)),
            new WatermarkTintOption(_localization["TintForest"], Color.FromRgb(0x16, 0x6A, 0x53)),
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

        RebuildTemplatesPreservingSelection(refreshPreview: false);

        _ = InitializeAsync();
    }

    public ObservableCollection<DocumentItemViewModel> Documents { get; }

    public ObservableCollection<WatermarkInputFieldViewModel> WatermarkLines { get; }

    public ObservableCollection<WatermarkInputFieldViewModel> TemplateFields { get; }

    public ObservableCollection<WatermarkTemplateDefinition> Templates { get; }

    public IReadOnlyList<int> LineCountOptions { get; }

    public List<WatermarkTintOption> TintOptions { get; }

    public bool IsPreviewEmpty => PreviewImage is null;

    public bool IsEmptyStateVisible => Documents.Count == 0;

    public bool IsCustomTemplateSelected => SelectedTemplate?.IsCustomMultiline == true;

    public bool IsTemplateFieldSectionVisible => !IsCustomTemplateSelected && TemplateFields.Count > 0;

    public string AppTitleText => _localization["AppTitle"];

    public string AppSubtitleText => _localization["AppSubtitle"];

    public string ImportText => _localization["Import"];

    public string SettingsText => _localization["Settings"];

    public string ItemsText => _localization["Items"];

    public string SecureDocumentsText => string.Format(_localization["SecureDocumentsFormat"], Documents.Count);

    public string MyDocumentsText => _localization["MyDocuments"];

    public string NoDocumentsText => _localization["NoDocuments"];

    public string NoDocumentsHintText => _localization["NoDocumentsHint"];

    public string WatermarkText => _localization["Watermark"];

    public string LivePreviewText => _localization["LivePreview"];

    public string TemplateText => _localization["Template"];

    public string TextLinesText => _localization["TextLines"];

    public string TintText => _localization["Tint"];

    public string OpacityText => _localization["Opacity"];

    public string FontSizeText => _localization["FontSize"];

    public string HorizontalSpacingText => _localization["HorizontalSpacing"];

    public string VerticalSpacingText => _localization["VerticalSpacing"];

    public string AngleText => _localization["Angle"];

    public string ExportText => _localization["Export"];

    public string RenameText => _localization["Rename"];

    public string DeleteActionText => _localization["Delete"];

    public string WorkingText => _localization["Working"];

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

    partial void OnSelectedTemplateChanged(WatermarkTemplateDefinition? value)
    {
        RebuildTemplateFields(value, refreshPreview: true);
        OnPropertyChanged(nameof(IsCustomTemplateSelected));
        OnPropertyChanged(nameof(IsTemplateFieldSectionVisible));
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
            Filter = _localization["FilterImages"],
            Multiselect = false,
            CheckFileExists = true,
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        string initialName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
        NicknameDialog nicknameDialog = new(_localization["DialogImportTitle"], _localization["DialogImageNameLabel"], initialName)
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
                _localization["ErrorDuplicateName"],
                _localization["ErrorTitle"],
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
            StatusMessage = _localization["StatusImporting"];

            DocumentEntry imported = await _documentVaultService.ImportAsync(
                openFileDialog.FileName,
                displayName,
                NameConflictBehavior.AskOverwrite,
                CancellationToken.None);

            await ReloadDocumentsAsync(imported.Id);
            StatusMessage = _localization["StatusImportSuccess"];
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

        string baseName = SanitizeFileName(SelectedDocument.DisplayName);
        string defaultName = string.Format(CultureInfo.CurrentCulture, _localization["ExportDefaultNameFormat"], baseName, DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture));

        var saveDialog = new SaveFileDialog
        {
            Filter = _localization["FilterExport"],
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
            StatusMessage = _localization["StatusExporting"];

            await _documentVaultService.ExportAsync(
                SelectedDocument.Id,
                BuildWatermarkOptions(),
                saveDialog.FileName,
                85,
                CancellationToken.None);

            StatusMessage = _localization["StatusExportSuccess"];
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

        NicknameDialog renameDialog = new(_localization["DialogRenameTitle"], _localization["DialogImageNameLabel"], target.DisplayName)
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
            StatusMessage = _localization["StatusRenaming"];

            await _documentVaultService.RenameAsync(target.Id, newName, CancellationToken.None);
            await ReloadDocumentsAsync(target.Id);

            StatusMessage = _localization["StatusRenameSuccess"];
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

        FluentConfirmDialog confirmDialog = new(
            _localization["DeleteTitle"],
            string.Format(_localization["DeletePromptFormat"], target.DisplayName),
            _localization["Delete"],
            _localization["Cancel"])
        {
            Owner = Application.Current.MainWindow,
        };

        bool shouldDelete = confirmDialog.ShowDialog() == true && confirmDialog.IsConfirmed;
        if (!shouldDelete)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = _localization["StatusDeleting"];

            await _documentVaultService.DeleteAsync(target.Id, CancellationToken.None);
            await ReloadDocumentsAsync(Guid.Empty);

            if (SelectedDocument?.Id == target.Id)
            {
                SelectedDocument = null;
                PreviewImage = null;
            }

            StatusMessage = _localization["StatusDeleteSuccess"];
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
    private void OpenSettings()
    {
        SettingsDialog settingsDialog = new()
        {
            Owner = Application.Current.MainWindow,
        };

        settingsDialog.ShowDialog();
    }

    [RelayCommand]
    private void SelectTemplate(WatermarkTemplateDefinition? template)
    {
        if (template is not null)
        {
            SelectedTemplate = template;
        }
    }

    [RelayCommand]
    private void SelectLineCount(int lineCount)
    {
        SelectedLineCount = lineCount;
    }

    [RelayCommand]
    private void SelectTintOption(WatermarkTintOption? tintOption)
    {
        if (tintOption is not null)
        {
            SelectedTintOption = tintOption;
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
            StatusMessage = _localization["StatusLoadingVault"];

            await ReloadDocumentsAsync(Guid.Empty);

            StatusMessage = Documents.Count == 0 ? _localization["StatusLoadEmpty"] : _localization["StatusLoadSuccess"];
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
        OnPropertyChanged(nameof(SecureDocumentsText));

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
            string initial = index < existingValues.Count ? existingValues[index] : index == 0 ? _localization["DefaultCustomLine1"] : string.Empty;
            var line = WatermarkInputFieldViewModel.CreateLine(index + 1, initial, _localization["LineLabelFormat"]);
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

    private void RebuildTemplateFields(WatermarkTemplateDefinition? template, bool refreshPreview)
    {
        foreach (WatermarkInputFieldViewModel field in TemplateFields)
        {
            field.PropertyChanged -= OnTemplateFieldPropertyChanged;
        }

        TemplateFields.Clear();

        if (template is not null && !template.IsCustomMultiline)
        {
            foreach (WatermarkTemplateFieldDefinition fieldDef in template.Fields)
            {
                string localizedLabel = _localization[fieldDef.Label];
                var field = new WatermarkInputFieldViewModel(localizedLabel, fieldDef.DefaultValue ?? string.Empty);
                field.PropertyChanged += OnTemplateFieldPropertyChanged;
                TemplateFields.Add(field);
            }
        }

        OnPropertyChanged(nameof(IsCustomTemplateSelected));
        OnPropertyChanged(nameof(IsTemplateFieldSectionVisible));

        if (refreshPreview)
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }

    private void OnLineInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WatermarkInputFieldViewModel.Value) && IsCustomTemplateSelected)
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }

    private void OnTemplateFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
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
        WatermarkTemplateDefinition template = SelectedTemplate ?? Templates[0];

        List<string> lines = template.IsCustomMultiline
            ? BuildCustomLines()
            : BuildTemplateLines(template);

        AppendValidityLine(lines);

        return new WatermarkOptions(
            lines,
            Opacity,
            FontSize,
            HorizontalSpacing,
            VerticalSpacing,
            AngleDegrees,
            SelectedTintOption?.Color ?? Color.FromRgb(0x25, 0x63, 0xEB),
            template.TemplateId,
            template.Version,
            null);
    }

    private List<string> BuildCustomLines()
    {
        List<string> lines = new(capacity: WatermarkLines.Count);
        foreach (WatermarkInputFieldViewModel line in WatermarkLines)
        {
            string value = line.Value?.Trim() ?? string.Empty;
            lines.Add(value);
        }

        return lines;
    }

    private List<string> BuildTemplateLines(WatermarkTemplateDefinition template)
    {
        string text = template.Template;

        for (int index = 0; index < template.Fields.Count && index < TemplateFields.Count; index++)
        {
            WatermarkTemplateFieldDefinition definition = template.Fields[index];
            string replacement = TemplateFields[index].Value?.Trim() ?? string.Empty;
            text = text.Replace($"{{{definition.Placeholder}}}", replacement, StringComparison.OrdinalIgnoreCase);
            text = text.Replace($"{{{{{definition.Placeholder}}}}}", replacement, StringComparison.OrdinalIgnoreCase);
        }

        text = text.Replace("{Date}", GetTemplateDateText(), StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{{Date}}", GetTemplateDateText(), StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{Machine}", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{{Machine}}", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);

        string[] split = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length == 0)
        {
            return [_localization["DefaultWatermarkFallback"]];
        }

        List<string> lines = new(capacity: Math.Min(5, split.Length));
        foreach (string line in split)
        {
            if (lines.Count >= 5)
            {
                break;
            }

            lines.Add(line);
        }

        return lines;
    }

    private void RebuildTemplatesPreservingSelection(bool refreshPreview)
    {
        string? selectedTemplateId = SelectedTemplate?.TemplateId;
        Templates.Clear();

        foreach (WatermarkTemplateDefinition template in BuildTemplates())
        {
            Templates.Add(template);
        }

        if (Templates.Count == 0)
        {
            SelectedTemplate = null;
            RebuildTemplateFields(null, refreshPreview);
            return;
        }

        WatermarkTemplateDefinition? resolved = null;
        if (!string.IsNullOrWhiteSpace(selectedTemplateId))
        {
            foreach (WatermarkTemplateDefinition template in Templates)
            {
                if (string.Equals(template.TemplateId, selectedTemplateId, StringComparison.Ordinal))
                {
                    resolved = template;
                    break;
                }
            }
        }

        SelectedTemplate = resolved ?? Templates[0];
        RebuildTemplateFields(SelectedTemplate, refreshPreview);
    }

    private IReadOnlyList<WatermarkTemplateDefinition> BuildTemplates()
    {
        return
        [
            new WatermarkTemplateDefinition(
                "standard-use",
                _localization["TemplateStandardUse"],
                1,
                _localization["TemplateStandardContent"],
                [new WatermarkTemplateFieldDefinition("Purpose", "Purpose", _localization["DefaultPurposeValue"])]),
            new WatermarkTemplateDefinition(
                "restricted",
                _localization["TemplateRestricted"],
                1,
                _localization["TemplateRestrictedContent"],
                [new WatermarkTemplateFieldDefinition("Recipient", "Recipient", _localization["DefaultRecipientValue"])]),
            new WatermarkTemplateDefinition(
                "application",
                _localization["TemplateApplication"],
                1,
                _localization["TemplateApplicationContent"],
                [new WatermarkTemplateFieldDefinition("System", "System", _localization["DefaultSystemValue"])]),
            new WatermarkTemplateDefinition(
                "cn-verification",
                _localization["TemplateVerification"],
                1,
                _localization["TemplateVerificationContent"],
                [
                    new WatermarkTemplateFieldDefinition("Purpose", "Purpose", _localization["DefaultPurposeValue"]),
                    new WatermarkTemplateFieldDefinition("Department", "Department", _localization["DefaultDepartmentValue"]),
                ]),
            new WatermarkTemplateDefinition(
                "cn-restricted-use",
                _localization["TemplateRestrictedUse"],
                1,
                _localization["TemplateRestrictedUseContent"],
                [
                    new WatermarkTemplateFieldDefinition("Recipient", "Recipient", _localization["DefaultRecipientValue"]),
                    new WatermarkTemplateFieldDefinition("Task", "Task", _localization["DefaultTaskValue"]),
                ]),
            new WatermarkTemplateDefinition(
                "custom-multi-line",
                _localization["TemplateCustom"],
                1,
                string.Empty,
                [],
                IsCustomMultiline: true),
        ];
    }

    
    private string GetTemplateDateText()
    {
        return _localization.CurrentLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
            ? DateTime.Now.ToString("yyyy/MM/dd")
            : DateTime.Now.ToString("yyyy-MM-dd");
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RebuildTemplatesPreservingSelection(refreshPreview: false);
        RebuildTemplateFields(SelectedTemplate, refreshPreview: false);
        RebuildLineInputs(SelectedLineCount, refreshPreview: false);
        RebuildTintOptions(refreshPreview: false);
        CustomDateText = GetTemplateDateText();
        CustomExpiryDateText = GetTemplateDateText();

        OnPropertyChanged(nameof(AppTitleText));
        OnPropertyChanged(nameof(AppSubtitleText));
        OnPropertyChanged(nameof(ImportText));
        OnPropertyChanged(nameof(SettingsText));
        OnPropertyChanged(nameof(ItemsText));
        OnPropertyChanged(nameof(SecureDocumentsText));
        OnPropertyChanged(nameof(MyDocumentsText));
        OnPropertyChanged(nameof(NoDocumentsText));
        OnPropertyChanged(nameof(NoDocumentsHintText));
        OnPropertyChanged(nameof(WatermarkText));
        OnPropertyChanged(nameof(LivePreviewText));
        OnPropertyChanged(nameof(TemplateText));
        OnPropertyChanged(nameof(TextLinesText));
        OnPropertyChanged(nameof(TintText));
        OnPropertyChanged(nameof(OpacityText));
        OnPropertyChanged(nameof(FontSizeText));
        OnPropertyChanged(nameof(HorizontalSpacingText));
        OnPropertyChanged(nameof(VerticalSpacingText));
        OnPropertyChanged(nameof(AngleText));
        OnPropertyChanged(nameof(ExportText));
        OnPropertyChanged(nameof(RenameText));
        OnPropertyChanged(nameof(DeleteActionText));
        OnPropertyChanged(nameof(WorkingText));
        OnPropertyChanged(nameof(BatchProcessText));
        OnPropertyChanged(nameof(SafeTransferText));
        OnPropertyChanged(nameof(CreateArchiveText));
        OnPropertyChanged(nameof(LoadArchiveText));
        OnPropertyChanged(nameof(SelectForBatchText));
        OnPropertyChanged(nameof(SelectedForBulkText));
        OnPropertyChanged(nameof(ValidityText));
        OnPropertyChanged(nameof(ValidityNoneText));
        OnPropertyChanged(nameof(ValidityDateText));
        OnPropertyChanged(nameof(ValidityExpiryDateText));
        OnPropertyChanged(nameof(DateOptionsText));
        OnPropertyChanged(nameof(ExpiryOptionsText));
        OnPropertyChanged(nameof(TodayText));
        OnPropertyChanged(nameof(ThisWeekText));
        OnPropertyChanged(nameof(ThisMonthText));
        OnPropertyChanged(nameof(CustomText));
    }
}






























