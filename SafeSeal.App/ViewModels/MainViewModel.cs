using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly WorkspaceStateService _workspaceState;
    private readonly ILoggingService _logger;
    private readonly ISafeTransferService _safeTransferService;
    private readonly ITransferPackageService _transferPackageService;
    private readonly HiddenVaultStorageService _hiddenStorageService;
    private readonly SemaphoreSlim _previewLock = new(1, 1);
    private CancellationTokenSource? _previewCts;
    private int _previewRequestId;
    private bool _handlingBatchSelection;
    private bool _isApplyingWorkspaceState;
    private bool _isRefreshingFromWorkspaceEvent;

    [ObservableProperty]
    private DocumentItemViewModel? selectedDocument;

    [ObservableProperty]
    private DocumentItemViewModel? previewDocument;

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
        _workspaceState = WorkspaceStateService.Instance;
        _logger = LogManager.SharedLogger;
        _safeTransferService = new SafeTransferService(SafeSealStorageOptions.CreateDefault(), _logger);
        _transferPackageService = new TransferPackageService();
        _hiddenStorageService = new HiddenVaultStorageService(SafeSealStorageOptions.CreateDefault());
        _workspaceState.Initialize();
        _localization.LanguageChanged += OnLanguageChanged;
        _workspaceState.WorkspaceChanged += OnWorkspaceChanged;
        HookTemplateFieldBindings();

        Documents = new ObservableCollection<DocumentItemViewModel>();
        WatermarkLines = new ObservableCollection<WatermarkInputFieldViewModel>();

        Templates = new ObservableCollection<WatermarkTemplateDefinition>();

        LineCountOptions = [1, 2, 3, 4, 5];
        TintOptions = new ObservableCollection<WatermarkTintOption>();

        WorkspacePreferences workspace = _workspaceState.GetSnapshot();

        selectedLineCount = Math.Clamp(workspace.SelectedLineCount, 1, 5);
        opacity = workspace.Opacity;
        fontSize = workspace.FontSize;
        horizontalSpacing = workspace.HorizontalSpacing;
        verticalSpacing = workspace.VerticalSpacing;
        angleDegrees = workspace.AngleDegrees;
        validityMode = ParseValidityMode(workspace.ValidityMode);
        datePreset = ParseDatePreset(workspace.DatePreset);
        expiryPreset = ParseDatePreset(workspace.ExpiryPreset);
        customDate = workspace.CustomDate;
        customExpiryDate = workspace.CustomExpiryDate;
        statusMessage = string.Empty;

        _isApplyingWorkspaceState = true;
        try
        {
            RebuildLineInputs(selectedLineCount, refreshPreview: false);
            ApplyWorkspaceCustomLines(workspace);

            RebuildTemplatesPreservingSelection(refreshPreview: false);
            ApplyWorkspaceTemplateSelection(workspace);

            RebuildTintOptions(refreshPreview: false);
            ApplyWorkspaceTintSelection(workspace);
        }
        finally
        {
            _isApplyingWorkspaceState = false;
        }

        UpdateWorkspaceStateFromViewModel();
        _ = InitializeAsync();
    }

    public ObservableCollection<DocumentItemViewModel> Documents { get; }

    public ObservableCollection<WatermarkInputFieldViewModel> WatermarkLines { get; }

    public ObservableCollection<WorkspaceTemplateFieldState> TemplateFields => _workspaceState.CurrentTemplateFields;

    public ObservableCollection<WatermarkTemplateDefinition> Templates { get; }

    public IReadOnlyList<int> LineCountOptions { get; }

    public ObservableCollection<WatermarkTintOption> TintOptions { get; }

    public bool IsPreviewEmpty => PreviewImage is null;

    public bool IsEmptyStateVisible => Documents.Count == 0;

    public bool IsCustomTemplateSelected => SelectedTemplate?.IsCustomMultiline == true;

    public bool IsTemplateFieldSectionVisible => !IsCustomTemplateSelected && TemplateFields.Count > 0;

    public string PreviewDocumentName => PreviewDocument?.DisplayName ?? string.Empty;

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
        if (_handlingBatchSelection)
        {
            return;
        }

        if (IsBatchModeEnabled)
        {
            if (value is null)
            {
                return;
            }

            value.IsBatchSelected = !value.IsBatchSelected;

            _handlingBatchSelection = true;
            try
            {
                _previewCts?.Cancel();
                PreviewDocument = null;
                IsDetailPaneOpen = false;
                PreviewImage = null;
                SelectedDocument = null;
            }
            finally
            {
                _handlingBatchSelection = false;
            }

            OnPropertyChanged(nameof(SelectedForBulkText));
            OnPropertyChanged(nameof(HasBatchSelection));
            OnPropertyChanged(nameof(IsBatchExportVisible));
            OnPropertyChanged(nameof(CanBatchExport));
            OnPropertyChanged(nameof(CanCreateTransfer));
            OnPropertyChanged(nameof(PreviewDocumentName));
            return;
        }

        PreviewDocument = value;
        IsDetailPaneOpen = value is not null;
        OnPropertyChanged(nameof(PreviewDocumentName));
        OnPropertyChanged(nameof(CanCreateTransfer));
        _ = RefreshPreviewAsync(value);
    }

    partial void OnPreviewImageChanged(BitmapSource? value)
    {
        OnPropertyChanged(nameof(IsPreviewEmpty));
    }

    partial void OnSelectedTintOptionChanged(WatermarkTintOption? value)
    {
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnSelectedTemplateChanged(WatermarkTemplateDefinition? value)
    {
        RebuildTemplateFields(value, refreshPreview: false);
        OnPropertyChanged(nameof(IsCustomTemplateSelected));
        OnPropertyChanged(nameof(IsTemplateFieldSectionVisible));
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnSelectedLineCountChanged(int value)
    {
        RebuildLineInputs(value, refreshPreview: false);
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnOpacityChanged(double value)
    {
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnFontSizeChanged(double value)
    {
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnHorizontalSpacingChanged(double value)
    {
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnVerticalSpacingChanged(double value)
    {
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnAngleDegreesChanged(double value)
    {
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
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
        DocumentItemViewModel? target = PreviewDocument ?? SelectedDocument;
        if (target is null)
        {
            return;
        }

        string baseName = SanitizeFileName(target.DisplayName);
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
                target.Id,
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
        PreviewDocument = null;
        SelectedDocument = null;
        PreviewImage = null;
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(PreviewDocumentName));
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
        OnPropertyChanged(nameof(SelectedForBulkText));
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(IsBatchExportVisible));
        OnPropertyChanged(nameof(CanBatchExport));
        OnPropertyChanged(nameof(CanCreateTransfer));

        if (Documents.Count == 0)
        {
            _previewCts?.Cancel();
            PreviewDocument = null;
            SelectedDocument = null;
            IsDetailPaneOpen = false;
            PreviewImage = null;
            OnPropertyChanged(nameof(PreviewDocumentName));
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

        if (IsBatchModeEnabled)
        {
            _previewCts?.Cancel();
            PreviewDocument = null;
            IsDetailPaneOpen = false;
            PreviewImage = null;
            SelectedDocument = null;
            OnPropertyChanged(nameof(PreviewDocumentName));
            return;
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
            _ = RefreshPreviewAsync(PreviewDocument);
        }
    }

    private void RebuildTemplateFields(WatermarkTemplateDefinition? template, bool refreshPreview)
    {
        List<WorkspaceTemplateFieldState> fields = [];

        if (template is not null && !template.IsCustomMultiline)
        {
            foreach (WatermarkTemplateFieldDefinition fieldDef in template.Fields)
            {
                string localizedLabel = _localization[fieldDef.Label];
                fields.Add(new WorkspaceTemplateFieldState(
                    fieldDef.Placeholder,
                    localizedLabel,
                    fieldDef.DefaultValue ?? string.Empty));
            }
        }

        _workspaceState.ConfigureTemplateFields(fields);
        HookTemplateFieldBindings();

        OnPropertyChanged(nameof(TemplateFields));
        OnPropertyChanged(nameof(IsCustomTemplateSelected));
        OnPropertyChanged(nameof(IsTemplateFieldSectionVisible));

        if (refreshPreview)
        {
            _ = RefreshPreviewAsync(PreviewDocument);
        }
    }

    private void HookTemplateFieldBindings()
    {
        foreach (WorkspaceTemplateFieldState field in _workspaceState.CurrentTemplateFields)
        {
            field.PropertyChanged -= OnTemplateFieldPropertyChanged;
            field.PropertyChanged += OnTemplateFieldPropertyChanged;
        }
    }

    private void OnTemplateFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceTemplateFieldState.Value) || _isApplyingWorkspaceState)
        {
            return;
        }

        OnPropertyChanged(nameof(TemplateFields));
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    private void OnLineInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WatermarkInputFieldViewModel.Value) && IsCustomTemplateSelected)
        {
            SyncWorkspaceState();
            _ = RefreshPreviewAsync(PreviewDocument);
        }
    }

    private async Task RefreshPreviewAsync(DocumentItemViewModel? item)
    {
        int requestId = Interlocked.Increment(ref _previewRequestId);
        _previewCts?.Cancel();

        if (IsBatchModeEnabled || item is null || !IsDetailPaneOpen)
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
                && PreviewDocument?.Id == item.Id)
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
        WorkspacePreferences workspace = _workspaceState.GetSnapshot();
        WatermarkTemplateDefinition template = ResolveTemplateById(workspace.TemplateId);

        List<string> lines = template.IsCustomMultiline
            ? BuildCustomLines(workspace)
            : BuildTemplateLines(template, workspace);

        AppendValidityLine(lines, workspace);

        WatermarkTintOption tint = ResolveTintOption(workspace.TintKey);

        return new WatermarkOptions(
            lines,
            workspace.Opacity,
            workspace.FontSize,
            workspace.HorizontalSpacing,
            workspace.VerticalSpacing,
            workspace.AngleDegrees,
            tint.Color,
            template.TemplateId,
            template.Version,
            null);
    }

    private List<string> BuildCustomLines(WorkspacePreferences workspace)
    {
        List<string> lines = new(capacity: Math.Min(5, workspace.CustomLines.Count));
        int maxLines = Math.Clamp(workspace.SelectedLineCount, 1, 5);

        foreach (string line in workspace.CustomLines)
        {
            if (lines.Count >= maxLines)
            {
                break;
            }

            lines.Add(SanitizeRenderedLine(line));
        }

        if (lines.Count == 0)
        {
            lines.Add(_localization["DefaultWatermarkFallback"]);
        }

        return lines;
    }

    private List<string> BuildTemplateLines(WatermarkTemplateDefinition template, WorkspacePreferences workspace)
    {
        string text = template.Template;

        Dictionary<string, string> activeValues = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkspaceTemplateFieldState field in _workspaceState.CurrentTemplateFields)
        {
            if (!string.IsNullOrWhiteSpace(field.Key))
            {
                activeValues[field.Key] = field.Value ?? string.Empty;
            }
        }

        if (activeValues.Count == 0)
        {
            foreach ((string key, string value) in workspace.TemplateFieldValues)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    activeValues[key] = value ?? string.Empty;
                }
            }
        }

        for (int index = 0; index < template.Fields.Count; index++)
        {
            WatermarkTemplateFieldDefinition definition = template.Fields[index];
            activeValues.TryGetValue(definition.Placeholder, out string? replacement);
            replacement ??= string.Empty;

            text = text.Replace($"{{{definition.Placeholder}}}", replacement, StringComparison.OrdinalIgnoreCase);
            text = text.Replace($"{{{{{definition.Placeholder}}}}}", replacement, StringComparison.OrdinalIgnoreCase);
        }

        text = text.Replace("{Date}", GetTemplateDateText(), StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{{Date}}", GetTemplateDateText(), StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{Machine}", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{{Machine}}", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("\\n", Environment.NewLine, StringComparison.Ordinal);
        text = StripUnresolvedTemplateMarkers(text);
        text = text.Replace("{{", string.Empty, StringComparison.Ordinal)
            .Replace("}}", string.Empty, StringComparison.Ordinal);

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

            lines.Add(SanitizeRenderedLine(line));
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

    private static string StripUnresolvedTemplateMarkers(string text)
    {
        string result = Regex.Replace(
            text,
            @"\{\{\s*[^{}\r\n]+\s*\}\}",
            string.Empty,
            RegexOptions.CultureInvariant);
        return Regex.Replace(
            result,
            @"\{\s*[^{}\r\n]+\s*\}",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static string SanitizeRenderedLine(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        return text.Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal);
    }

    private void SyncWorkspaceState()
    {
        if (_isApplyingWorkspaceState)
        {
            return;
        }

        UpdateWorkspaceStateFromViewModel();
    }

    private void UpdateWorkspaceStateFromViewModel()
    {
        _workspaceState.Apply(BuildWorkspaceSnapshotFromViewModel());
    }

    private WorkspacePreferences BuildWorkspaceSnapshotFromViewModel()
    {
        Dictionary<string, string> templateValues = _workspaceState.GetTemplateFieldValuesSnapshot();

        List<string> customLines = WatermarkLines
            .Select(static line => line.Value ?? string.Empty)
            .Take(Math.Clamp(SelectedLineCount, 1, 5))
            .ToList();

        return new WorkspacePreferences
        {
            TemplateId = SelectedTemplate?.TemplateId ?? "custom-multi-line",
            SelectedLineCount = Math.Clamp(SelectedLineCount, 1, 5),
            TemplateFieldValues = templateValues,
            CustomLines = customLines,
            TintKey = SelectedTintOption?.Key ?? "blue",
            Opacity = Opacity,
            FontSize = FontSize,
            HorizontalSpacing = HorizontalSpacing,
            VerticalSpacing = VerticalSpacing,
            AngleDegrees = AngleDegrees,
            ValidityMode = ToWorkspaceValue(ValidityMode),
            DatePreset = ToWorkspaceValue(DatePreset),
            ExpiryPreset = ToWorkspaceValue(ExpiryPreset),
            CustomDate = CustomDate,
            CustomExpiryDate = CustomExpiryDate,
        };
    }

    private void ApplyWorkspaceCustomLines(WorkspacePreferences workspace)
    {
        int lineCount = Math.Clamp(workspace.SelectedLineCount, 1, 5);

        while (WatermarkLines.Count < lineCount)
        {
            WatermarkLines.Add(WatermarkInputFieldViewModel.CreateLine(WatermarkLines.Count + 1, string.Empty, _localization["LineLabelFormat"]));
        }

        for (int index = 0; index < lineCount && index < WatermarkLines.Count; index++)
        {
            string value = index < workspace.CustomLines.Count
                ? workspace.CustomLines[index]
                : index == 0
                    ? _localization["DefaultCustomLine1"]
                    : string.Empty;

            WatermarkLines[index].Value = value;
        }
    }

    private void ApplyWorkspaceTemplateSelection(WorkspacePreferences workspace)
    {
        WatermarkTemplateDefinition template = ResolveTemplateById(workspace.TemplateId);
        SelectedTemplate = template;

        _workspaceState.ApplyTemplateFieldValues(workspace.TemplateFieldValues);
    }

    private void ApplyWorkspaceTintSelection(WorkspacePreferences workspace)
    {
        SelectedTintOption = ResolveTintOption(workspace.TintKey);
    }

    private WatermarkTemplateDefinition ResolveTemplateById(string? templateId)
    {
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            WatermarkTemplateDefinition? matched = Templates.FirstOrDefault(x => string.Equals(x.TemplateId, templateId, StringComparison.Ordinal));
            if (matched is not null)
            {
                return matched;
            }
        }

        return Templates.First();
    }

    private WatermarkTintOption ResolveTintOption(string? tintKey)
    {
        if (!string.IsNullOrWhiteSpace(tintKey))
        {
            WatermarkTintOption? tint = TintOptions.FirstOrDefault(x => string.Equals(x.Key, tintKey, StringComparison.OrdinalIgnoreCase));
            if (tint is not null)
            {
                return tint;
            }
        }

        return TintOptions.First();
    }

    private static WatermarkValidityMode ParseValidityMode(string? value)
    {
        return value switch
        {
            "Date" => WatermarkValidityMode.Date,
            "ExpiryDate" => WatermarkValidityMode.ExpiryDate,
            _ => WatermarkValidityMode.None,
        };
    }

    private static WatermarkDatePreset ParseDatePreset(string? value)
    {
        return value switch
        {
            "ThisWeek" => WatermarkDatePreset.ThisWeek,
            "ThisMonth" => WatermarkDatePreset.ThisMonth,
            "Custom" => WatermarkDatePreset.Custom,
            _ => WatermarkDatePreset.Today,
        };
    }

    private static string ToWorkspaceValue(WatermarkValidityMode mode)
    {
        return mode switch
        {
            WatermarkValidityMode.Date => "Date",
            WatermarkValidityMode.ExpiryDate => "ExpiryDate",
            _ => "None",
        };
    }

    private static string ToWorkspaceValue(WatermarkDatePreset preset)
    {
        return preset switch
        {
            WatermarkDatePreset.ThisWeek => "ThisWeek",
            WatermarkDatePreset.ThisMonth => "ThisMonth",
            WatermarkDatePreset.Custom => "Custom",
            _ => "Today",
        };
    }


    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_isApplyingWorkspaceState || _isRefreshingFromWorkspaceEvent)
        {
            return;
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => OnWorkspaceChanged(sender, e));
            return;
        }

        _isRefreshingFromWorkspaceEvent = true;
        try
        {
            _ = RefreshPreviewAsync(PreviewDocument);
        }
        finally
        {
            _isRefreshingFromWorkspaceEvent = false;
        }
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        WorkspacePreferences snapshot = _workspaceState.GetSnapshot();
        _isApplyingWorkspaceState = true;
        try
        {
            RebuildTemplatesPreservingSelection(refreshPreview: false);
            RebuildTemplateFields(SelectedTemplate, refreshPreview: false);
            RebuildLineInputs(SelectedLineCount, refreshPreview: false);
            RebuildTintOptions(refreshPreview: false);

            ApplyWorkspaceTemplateSelection(snapshot);
            ApplyWorkspaceCustomLines(snapshot);
            ApplyWorkspaceTintSelection(snapshot);
        }
        finally
        {
            _isApplyingWorkspaceState = false;
        }

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
        OnPropertyChanged(nameof(BatchExportText));
        OnPropertyChanged(nameof(TransferText));
        OnPropertyChanged(nameof(SafeTransferText));
        OnPropertyChanged(nameof(CreateTransferText));
        OnPropertyChanged(nameof(LoadTransferText));
        OnPropertyChanged(nameof(CreateArchiveText));
        OnPropertyChanged(nameof(LoadArchiveText));
        OnPropertyChanged(nameof(SelectForBatchText));
        OnPropertyChanged(nameof(SelectedForBulkText));
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(IsBatchExportVisible));
        OnPropertyChanged(nameof(CanBatchExport));
        OnPropertyChanged(nameof(CanCreateTransfer));
        OnPropertyChanged(nameof(PreviewDocumentName));
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

        _ = RefreshPreviewAsync(PreviewDocument);
    }
}







































