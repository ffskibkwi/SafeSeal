using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SafeSeal.App.Dialogs;
using SafeSeal.App.Services;
using SafeSeal.Core;

namespace SafeSeal.App.ViewModels;

public enum WatermarkValidityMode
{
    None,
    Date,
    ExpiryDate,
}

public enum WatermarkDatePreset
{
    Today,
    ThisWeek,
    ThisMonth,
    Custom,
}

public enum WatermarkDateDisplayFormat
{
    Iso,
    EnglishShortMonth,
    Slash,
}

public partial class MainViewModel
{
    private static readonly byte[] Sstrans2Magic = "SSTRANS2"u8.ToArray();

    private enum TransferOperationPhase
    {
        Selection,
        Pin,
        Crypto,
        Staging,
        Import,
        Finalize,
    }

    [ObservableProperty]
    private WatermarkValidityMode validityMode = WatermarkValidityMode.None;

    [ObservableProperty]
    private WatermarkDatePreset datePreset = WatermarkDatePreset.Today;

    [ObservableProperty]
    private WatermarkDatePreset expiryPreset = WatermarkDatePreset.Today;

    [ObservableProperty]
    private DateTime? customDate = DateTime.Today;

    [ObservableProperty]
    private DateTime? customExpiryDate = DateTime.Today;

    [ObservableProperty]
    private WatermarkDateDisplayFormat dateDisplayFormat = WatermarkDateDisplayFormat.Iso;

    [ObservableProperty]
    private bool isBatchModeEnabled;

    public string BatchProcessText => _localization["BatchProcess"];

    public string BatchExportText => _localization["BatchExport"];

    public string BatchDeleteText => _localization["BatchDelete"];

    public bool IsBatchExportVisible => IsBatchModeEnabled && HasBatchSelection;

    public bool IsBatchDeleteVisible => IsBatchModeEnabled && HasBatchSelection;

    public bool HasBatchSelection => Documents.Any(static x => x.IsBatchSelected);

    public bool CanBatchExport => !IsBusy && HasBatchSelection;

    public bool CanBatchDelete => !IsBusy && HasBatchSelection;

    public bool CanCreateTransfer => !IsBusy && GetEffectiveSelectionCount() >= 1;

    public string TransferText => _localization["Transfer"];

    // Keep aliases for existing bindings.
    public string SafeTransferText => TransferText;

    public string CreateTransferText => _localization["CreateTransfer"];

    public string LoadTransferText => _localization["LoadTransfer"];

    // Keep aliases for existing bindings.
    public string CreateArchiveText => CreateTransferText;

    public string LoadArchiveText => LoadTransferText;

    public string SelectForBatchText => _localization["SelectForBatch"];

    public string SelectedForBulkText => string.Format(CultureInfo.CurrentCulture, _localization["SelectedForBulkFormat"], Documents.Count(static x => x.IsBatchSelected));

    public string ValidityText => _localization["Validity"];

    public string ValidityNoneText => _localization["ValidityNone"];

    public string ValidityDateText => _localization["ValidityDate"];

    public string ValidityExpiryDateText => _localization["ValidityExpiryDate"];

    public string DateOptionsText => _localization["DateOptions"];

    public string ExpiryOptionsText => _localization["ExpiryOptions"];

    public string TodayText => _localization["Today"];

    public string ThisWeekText => _localization["ThisWeek"];

    public string ThisMonthText => _localization["ThisMonth"];

    public string CustomText => _localization["Custom"];

    public string DateFormatText => _localization["DateFormat"];

    public string DateFormatIsoText => _localization["DateFormatIso"];

    public string DateFormatMonthText => _localization["DateFormatMonth"];

    public string DateFormatSlashText => _localization["DateFormatSlash"];

    public bool IsValidityNone
    {
        get => ValidityMode == WatermarkValidityMode.None;
        set
        {
            if (value)
            {
                ValidityMode = WatermarkValidityMode.None;
            }
        }
    }

    public bool IsValidityDate
    {
        get => ValidityMode == WatermarkValidityMode.Date;
        set
        {
            if (value)
            {
                ValidityMode = WatermarkValidityMode.Date;
            }
        }
    }

    public bool IsValidityExpiryDate
    {
        get => ValidityMode == WatermarkValidityMode.ExpiryDate;
        set
        {
            if (value)
            {
                ValidityMode = WatermarkValidityMode.ExpiryDate;
            }
        }
    }

    public bool IsDateOptionsVisible => ValidityMode == WatermarkValidityMode.Date;

    public bool IsExpiryOptionsVisible => ValidityMode == WatermarkValidityMode.ExpiryDate;

    public bool IsDatePresetToday
    {
        get => DatePreset == WatermarkDatePreset.Today;
        set
        {
            if (value)
            {
                DatePreset = WatermarkDatePreset.Today;
            }
        }
    }

    public bool IsDatePresetCustom
    {
        get => DatePreset == WatermarkDatePreset.Custom;
        set
        {
            if (value)
            {
                DatePreset = WatermarkDatePreset.Custom;
            }
        }
    }

    public bool IsDateCustomVisible => IsDateOptionsVisible && DatePreset == WatermarkDatePreset.Custom;

    public bool IsExpiryPresetToday
    {
        get => ExpiryPreset == WatermarkDatePreset.Today;
        set
        {
            if (value)
            {
                ExpiryPreset = WatermarkDatePreset.Today;
            }
        }
    }

    public bool IsExpiryPresetThisWeek
    {
        get => ExpiryPreset == WatermarkDatePreset.ThisWeek;
        set
        {
            if (value)
            {
                ExpiryPreset = WatermarkDatePreset.ThisWeek;
            }
        }
    }

    public bool IsExpiryPresetThisMonth
    {
        get => ExpiryPreset == WatermarkDatePreset.ThisMonth;
        set
        {
            if (value)
            {
                ExpiryPreset = WatermarkDatePreset.ThisMonth;
            }
        }
    }

    public bool IsExpiryPresetCustom
    {
        get => ExpiryPreset == WatermarkDatePreset.Custom;
        set
        {
            if (value)
            {
                ExpiryPreset = WatermarkDatePreset.Custom;
            }
        }
    }

    public bool IsExpiryCustomVisible => IsExpiryOptionsVisible && ExpiryPreset == WatermarkDatePreset.Custom;

    public bool IsDateFormatOptionsVisible => ValidityMode != WatermarkValidityMode.None;

    public bool IsDateFormatIso
    {
        get => DateDisplayFormat == WatermarkDateDisplayFormat.Iso;
        set
        {
            if (value)
            {
                DateDisplayFormat = WatermarkDateDisplayFormat.Iso;
            }
        }
    }

    public bool IsDateFormatMonthText
    {
        get => DateDisplayFormat == WatermarkDateDisplayFormat.EnglishShortMonth;
        set
        {
            if (value)
            {
                DateDisplayFormat = WatermarkDateDisplayFormat.EnglishShortMonth;
            }
        }
    }

    public bool IsDateFormatSlash
    {
        get => DateDisplayFormat == WatermarkDateDisplayFormat.Slash;
        set
        {
            if (value)
            {
                DateDisplayFormat = WatermarkDateDisplayFormat.Slash;
            }
        }
    }
    partial void OnIsBatchModeEnabledChanged(bool value)
    {
        ClearBatchSelectionState();

        if (value)
        {
            _previewCts?.Cancel();
            _handlingBatchSelection = true;
            try
            {
                PreviewDocument = null;
                IsDetailPaneOpen = false;
                PreviewImage = null;
                SelectedDocument = null;
            }
            finally
            {
                _handlingBatchSelection = false;
            }
        }
        else if (Documents.Count > 0 && SelectedDocument is null)
        {
            SelectedDocument = Documents[0];
        }

        OnPropertyChanged(nameof(IsBatchExportVisible));
        OnPropertyChanged(nameof(IsBatchDeleteVisible));
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(CanBatchExport));
        OnPropertyChanged(nameof(CanBatchDelete));
        OnPropertyChanged(nameof(CanCreateTransfer));
        OnPropertyChanged(nameof(SelectedForBulkText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBatchExport));
        OnPropertyChanged(nameof(CanBatchDelete));
        OnPropertyChanged(nameof(CanCreateTransfer));
        OnPropertyChanged(nameof(IsBatchExportVisible));
        OnPropertyChanged(nameof(IsBatchDeleteVisible));
    }
    partial void OnValidityModeChanged(WatermarkValidityMode value)
    {
        OnPropertyChanged(nameof(IsValidityNone));
        OnPropertyChanged(nameof(IsValidityDate));
        OnPropertyChanged(nameof(IsValidityExpiryDate));
        OnPropertyChanged(nameof(IsDateOptionsVisible));
        OnPropertyChanged(nameof(IsExpiryOptionsVisible));
        OnPropertyChanged(nameof(IsDateFormatOptionsVisible));
        OnPropertyChanged(nameof(IsDateCustomVisible));
        OnPropertyChanged(nameof(IsExpiryCustomVisible));
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnDatePresetChanged(WatermarkDatePreset value)
    {
        OnPropertyChanged(nameof(IsDatePresetToday));
        OnPropertyChanged(nameof(IsDatePresetCustom));
        OnPropertyChanged(nameof(IsDateCustomVisible));
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnExpiryPresetChanged(WatermarkDatePreset value)
    {
        OnPropertyChanged(nameof(IsExpiryPresetToday));
        OnPropertyChanged(nameof(IsExpiryPresetThisWeek));
        OnPropertyChanged(nameof(IsExpiryPresetThisMonth));
        OnPropertyChanged(nameof(IsExpiryPresetCustom));
        OnPropertyChanged(nameof(IsExpiryCustomVisible));
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    partial void OnCustomDateChanged(DateTime? value)
    {
        SyncWorkspaceState();
        if (DatePreset == WatermarkDatePreset.Custom)
        {
            _ = RefreshPreviewAsync(PreviewDocument);
        }
    }

    partial void OnCustomExpiryDateChanged(DateTime? value)
    {
        SyncWorkspaceState();
        if (ExpiryPreset == WatermarkDatePreset.Custom)
        {
            _ = RefreshPreviewAsync(PreviewDocument);
        }
    }

    partial void OnDateDisplayFormatChanged(WatermarkDateDisplayFormat value)
    {
        OnPropertyChanged(nameof(IsDateFormatIso));
        OnPropertyChanged(nameof(IsDateFormatMonthText));
        OnPropertyChanged(nameof(IsDateFormatSlash));
        SyncWorkspaceState();
        _ = RefreshPreviewAsync(PreviewDocument);
    }

    [RelayCommand]
    private void ToggleBatchMode()
    {
        IsBatchModeEnabled = !IsBatchModeEnabled;
    }

    [RelayCommand]
    private async Task BatchExportAsync()
    {
        IReadOnlyList<DocumentItemViewModel> selectedItems = Documents.Where(static x => x.IsBatchSelected).ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        OpenFolderDialog folderDialog = new()
        {
            Title = _localization["BatchExportFolderTitle"],
        };

        if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
        {
            return;
        }

        string outputDirectory = folderDialog.FolderName;
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        HashSet<string> usedPaths = new(StringComparer.OrdinalIgnoreCase);
        int completed = 0;
        int failed = 0;

        try
        {
            IsBusy = true;
            StatusMessage = _localization["StatusBatchPreparing"];
            WatermarkOptions options = BuildWatermarkOptions();

            foreach (DocumentItemViewModel item in selectedItems)
            {
                string extension = ResolveBatchExportExtension(item.OriginalExtension);
                string outputPath = BuildBatchExportPath(outputDirectory, item.DisplayName, timestamp, extension, usedPaths);

                try
                {
                    await _documentVaultService.ExportAsync(item.Id, options, outputPath, 85, CancellationToken.None);
                    completed++;
                    StatusMessage = string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchProgressFormat"], completed + failed, selectedItems.Count);
                }
                catch
                {
                    failed++;
                }
            }

            StatusMessage = failed == 0
                ? string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchCompletedFormat"], completed, outputDirectory)
                : string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchCompletedWithErrorsFormat"], completed, failed, outputDirectory);
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
    private async Task BatchDeleteAsync()
    {
        IReadOnlyList<DocumentItemViewModel> selectedItems = Documents.Where(static x => x.IsBatchSelected).ToList();
        if (selectedItems.Count == 0 || IsBusy)
        {
            return;
        }

        FluentConfirmDialog confirmDialog = new(
            _localization["BatchDeleteTitle"],
            string.Format(CultureInfo.CurrentCulture, _localization["BatchDeletePromptFormat"], selectedItems.Count),
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

        int completed = 0;
        int failed = 0;

        try
        {
            IsBusy = true;
            StatusMessage = _localization["StatusBatchDeletePreparing"];

            foreach (DocumentItemViewModel item in selectedItems)
            {
                try
                {
                    await _documentVaultService.DeleteAsync(item.Id, CancellationToken.None);
                    completed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.Warning(
                        nameof(MainViewModel),
                        "batch_delete_item_failed",
                        fields: new Dictionary<string, object?>
                        {
                            ["documentId"] = item.Id,
                        },
                        exception: ex);
                }
            }

            await ReloadDocumentsAsync(Guid.Empty);
            ClearBatchSelectionState();

            StatusMessage = failed == 0
                ? string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchDeleteCompletedFormat"], completed)
                : string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchDeleteCompletedWithErrorsFormat"], completed, failed);
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsBatchDeleteVisible));
            OnPropertyChanged(nameof(CanBatchDelete));
            OnPropertyChanged(nameof(IsBatchExportVisible));
            OnPropertyChanged(nameof(CanBatchExport));
        }
    }
    [RelayCommand]
    private async Task CreateArchiveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        TransferOperationPhase phase = TransferOperationPhase.Selection;
        string tempDirectory = Path.Combine(Path.GetTempPath(), "SafeSeal", "transfer", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        _logger.Info(
            nameof(MainViewModel),
            "transfer_create_start",
            operationId,
            new Dictionary<string, object?>
            {
                ["isBatchMode"] = IsBatchModeEnabled,
                ["tempDirectory"] = tempDirectory,
            });

        try
        {
            IReadOnlyList<DocumentItemViewModel> selectedItems = GetTransferSelectionItems();
            if (selectedItems.Count == 0)
            {
                _logger.Info(nameof(MainViewModel), "transfer_create_cancelled_no_selection", operationId);
                return;
            }

            _logger.Trace(
                nameof(MainViewModel),
                "transfer_create_selection_resolved",
                operationId,
                new Dictionary<string, object?>
                {
                    ["selectedCount"] = selectedItems.Count,
                });

            phase = TransferOperationPhase.Pin;
            string? pin = PromptForPin();
            if (!IsValidTransferPin(pin))
            {
                _logger.Warning(
                    nameof(MainViewModel),
                    "transfer_create_pin_invalid",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                    });

                return;
            }

            _logger.Trace(nameof(MainViewModel), "transfer_create_pin_valid", operationId);

            IReadOnlyList<DocumentEntry> allEntries = await _documentVaultService.ListAsync(CancellationToken.None);
            List<DocumentEntry> selectedEntries = ResolveEntriesByIds(allEntries, selectedItems.Select(static x => x.Id));
            if (selectedEntries.Count == 0)
            {
                _logger.Warning(nameof(MainViewModel), "transfer_create_selection_unresolved", operationId);
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
            bool isMultiPackage = selectedEntries.Count > 1;
            string defaultName = selectedEntries.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, _localization["TransferDefaultSingleArchiveFormat"], SanitizeFileName(selectedEntries[0].DisplayName), timestamp)
                : string.Format(CultureInfo.CurrentCulture, _localization["TransferDefaultMultiArchiveFormat"], timestamp);

            SaveFileDialog saveDialog = new()
            {
                Filter = _localization["FilterTransferArchive"],
                FilterIndex = isMultiPackage ? 2 : 1,
                DefaultExt = isMultiPackage ? ".sstrans2" : ".sstransfer",
                FileName = defaultName,
                AddExtension = true,
            };

            if (saveDialog.ShowDialog() != true)
            {
                _logger.Info(nameof(MainViewModel), "transfer_create_cancelled_file_dialog", operationId);
                return;
            }

            await SetTransferBusyStateAsync(true, _localization["StatusTransferPreparing"]);

            if (selectedEntries.Count == 1)
            {
                phase = TransferOperationPhase.Crypto;
                _logger.Info(
                    nameof(MainViewModel),
                    "transfer_create_single_crypto_start",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                        ["outputPath"] = saveDialog.FileName,
                    });

                await Task.Run(
                    async () => await _safeTransferService.CreateArchiveAsync(selectedEntries[0], saveDialog.FileName, pin!, CancellationToken.None),
                    CancellationToken.None);
            }
            else
            {
                phase = TransferOperationPhase.Staging;
                List<string> tempFiles = await MaterializeTempFilesAsync(selectedEntries, tempDirectory, CancellationToken.None);

                _logger.Info(
                    nameof(MainViewModel),
                    "transfer_create_temp_staging_completed",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                        ["tempFileCount"] = tempFiles.Count,
                        ["requestedCount"] = selectedEntries.Count,
                    });

                if (tempFiles.Count == 0)
                {
                    _logger.Warning(nameof(MainViewModel), "transfer_create_temp_staging_empty", operationId);
                    return;
                }

                phase = TransferOperationPhase.Crypto;
                IProgress<BatchProgress> progress = new Progress<BatchProgress>(p =>
                {
                    _ = UpdateStatusOnUiAsync(string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferProgressFormat"], p.Completed, p.Total));
                });

                await Task.Run(
                    async () => await _transferPackageService.CreateMergedPackageAsync(tempFiles, pin!, saveDialog.FileName, progress, CancellationToken.None),
                    CancellationToken.None);

                _logger.Info(
                    nameof(MainViewModel),
                    "transfer_create_sstrans2_written",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                        ["tempFileCount"] = tempFiles.Count,
                        ["outputPath"] = saveDialog.FileName,
                    });
            }

            phase = TransferOperationPhase.Finalize;
            await UpdateStatusOnUiAsync(string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferCreateSuccess"], saveDialog.FileName));

            _logger.Info(
                nameof(MainViewModel),
                "transfer_create_completed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase.ToString(),
                    ["outputPath"] = saveDialog.FileName,
                    ["itemCount"] = selectedEntries.Count,
                });
        }
        catch (Exception ex)
        {
            _logger.Error(
                nameof(MainViewModel),
                "transfer_create_failed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase.ToString(),
                },
                ex);

            await HandleTransferOperationFailureAsync(ex, phase, isLoadOperation: false);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);

            _logger.Trace(
                nameof(MainViewModel),
                "transfer_create_temp_cleanup",
                operationId,
                new Dictionary<string, object?>
                {
                    ["tempDirectory"] = tempDirectory,
                });

            await SetTransferBusyStateAsync(false, null);
        }
    }

    [RelayCommand]
    private async Task LoadArchiveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        OpenFileDialog openDialog = new()
        {
            Filter = _localization["FilterTransferArchive"],
            Multiselect = false,
            CheckFileExists = true,
        };

        if (openDialog.ShowDialog() != true)
        {
            return;
        }

        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        TransferOperationPhase phase = TransferOperationPhase.Pin;

        _logger.Info(
            nameof(MainViewModel),
            "transfer_load_start",
            operationId,
            new Dictionary<string, object?>
            {
                ["archivePath"] = openDialog.FileName,
            });

        string? pin = PromptForPin();
        if (!IsValidTransferPin(pin))
        {
            _logger.Warning(
                nameof(MainViewModel),
                "transfer_load_pin_invalid",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase.ToString(),
                });

            return;
        }

        _logger.Trace(nameof(MainViewModel), "transfer_load_pin_valid", operationId);

        string tempDirectory = Path.Combine(Path.GetTempPath(), "SafeSeal", "transfer-import", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        string tempExtractDirectory = Path.Combine(tempDirectory, "extract");
        List<DocumentEntry> importedEntries = [];
        int extractedErrorCount = 0;

        try
        {
            await SetTransferBusyStateAsync(true, _localization["StatusTransferPreparing"]);

            if (_safeTransferService.CanReadFormat(openDialog.FileName))
            {
                phase = TransferOperationPhase.Crypto;
                _logger.Info(nameof(MainViewModel), "transfer_load_format_sstransfer", operationId);

                TransferArchiveContent content = await Task.Run(
                    async () => await _safeTransferService.ExtractArchiveAsync(openDialog.FileName, pin!, CancellationToken.None),
                    CancellationToken.None);

                try
                {
                    phase = TransferOperationPhase.Staging;
                    Directory.CreateDirectory(tempDirectory);

                    string importName = ResolveTransferImportName(content.OriginalFileName);
                    string extension = ResolveTransferImportExtension(content.OriginalFileName, content.MimeType);
                    string tempPath = BuildUniqueFilePath(tempDirectory, SanitizeFileName(importName), extension);

                    await File.WriteAllBytesAsync(tempPath, content.ImageData, CancellationToken.None);

                    _logger.Info(
                        nameof(MainViewModel),
                        "transfer_load_single_staged",
                        operationId,
                        new Dictionary<string, object?>
                        {
                            ["phase"] = phase.ToString(),
                            ["tempPath"] = tempPath,
                            ["imageBytes"] = content.ImageData.LongLength,
                        });

                    phase = TransferOperationPhase.Import;
                    DocumentEntry imported = await _documentVaultService.ImportAsync(
                        tempPath,
                        importName,
                        NameConflictBehavior.AutoSuffix,
                        CancellationToken.None);

                    importedEntries.Add(imported);

                    _logger.Info(
                        nameof(MainViewModel),
                        "transfer_load_single_imported",
                        operationId,
                        new Dictionary<string, object?>
                        {
                            ["phase"] = phase.ToString(),
                            ["documentId"] = imported.Id,
                            ["importName"] = importName,
                        });
                }
                finally
                {
                    Array.Clear(content.ImageData, 0, content.ImageData.Length);
                    _logger.Trace(nameof(MainViewModel), "transfer_load_single_content_cleared", operationId);
                }
            }
            else if (IsSstrans2Archive(openDialog.FileName))
            {
                phase = TransferOperationPhase.Crypto;
                _logger.Info(nameof(MainViewModel), "transfer_load_format_sstrans2", operationId);

                Directory.CreateDirectory(tempExtractDirectory);

                IProgress<BatchProgress> progress = new Progress<BatchProgress>(p =>
                {
                    _ = UpdateStatusOnUiAsync(string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferProgressFormat"], p.Completed, p.Total));
                });

                BatchResult result = await Task.Run(
                    async () => await _transferPackageService.ExtractMergedPackageAsync(
                        openDialog.FileName,
                        pin!,
                        tempExtractDirectory,
                        progress,
                        CancellationToken.None),
                    CancellationToken.None);

                extractedErrorCount += result.Errors.Count;

                _logger.Info(
                    nameof(MainViewModel),
                    "transfer_load_sstrans2_extract_completed",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                        ["outputFileCount"] = result.OutputFiles.Count,
                        ["extractErrorCount"] = result.Errors.Count,
                        ["continueStrategy"] = "continue_summary",
                    });

                phase = TransferOperationPhase.Import;
                foreach (string extractedFile in result.OutputFiles)
                {
                    try
                    {
                        string importName = ResolveTransferImportName(extractedFile);
                        DocumentEntry imported = await _documentVaultService.ImportAsync(
                            extractedFile,
                            importName,
                            NameConflictBehavior.AutoSuffix,
                            CancellationToken.None);

                        importedEntries.Add(imported);

                        _logger.Trace(
                            nameof(MainViewModel),
                            "transfer_load_sstrans2_file_imported",
                            operationId,
                            new Dictionary<string, object?>
                            {
                                ["phase"] = phase.ToString(),
                                ["sourceFile"] = extractedFile,
                                ["documentId"] = imported.Id,
                            });
                    }
                    catch (Exception ex)
                    {
                        extractedErrorCount++;

                        _logger.Warning(
                            nameof(MainViewModel),
                            "transfer_load_sstrans2_file_import_failed",
                            operationId,
                            new Dictionary<string, object?>
                            {
                                ["phase"] = phase.ToString(),
                                ["sourceFile"] = extractedFile,
                                ["continueStrategy"] = "continue_summary",
                            },
                            ex);
                    }
                }
            }
            else
            {
                _logger.Warning(nameof(MainViewModel), "transfer_load_unknown_format", operationId);
                await UpdateStatusOnUiAsync(_localization["ErrorTransferUnknownFormat"]);
                return;
            }

            phase = TransferOperationPhase.Finalize;
            if (importedEntries.Count > 0)
            {
                DocumentEntry lastImported = importedEntries[^1];
                await ReloadDocumentsAsync(lastImported.Id);

                string summary = extractedErrorCount == 0
                    ? string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferImportVaultSuccessFormat"], importedEntries.Count)
                    : string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferImportVaultPartialFormat"], importedEntries.Count, extractedErrorCount);

                await UpdateStatusOnUiAsync(summary);

                _logger.Info(
                    nameof(MainViewModel),
                    "transfer_load_completed",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                        ["importedCount"] = importedEntries.Count,
                        ["errorCount"] = extractedErrorCount,
                        ["continueStrategy"] = "continue_summary",
                    });
            }
            else
            {
                await UpdateStatusOnUiAsync(_localization["StatusTransferImportVaultNone"]);

                _logger.Warning(
                    nameof(MainViewModel),
                    "transfer_load_no_files_imported",
                    operationId,
                    new Dictionary<string, object?>
                    {
                        ["phase"] = phase.ToString(),
                        ["errorCount"] = extractedErrorCount,
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                nameof(MainViewModel),
                "transfer_load_failed",
                operationId,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase.ToString(),
                },
                ex);

            await HandleTransferOperationFailureAsync(ex, phase, isLoadOperation: true);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);

            _logger.Trace(
                nameof(MainViewModel),
                "transfer_load_temp_cleanup",
                operationId,
                new Dictionary<string, object?>
                {
                    ["tempDirectory"] = tempDirectory,
                });

            await SetTransferBusyStateAsync(false, null);
        }
    }

    private IReadOnlyList<DocumentItemViewModel> GetTransferSelectionItems()
    {
        if (IsBatchModeEnabled)
        {
            return Documents.Where(static x => x.IsBatchSelected).ToList();
        }

        DocumentItemViewModel? single = SelectedDocument ?? PreviewDocument;
        return single is null ? [] : [single];
    }

    private int GetEffectiveSelectionCount()
    {
        if (IsBatchModeEnabled)
        {
            return Documents.Count(static x => x.IsBatchSelected);
        }

        return (SelectedDocument ?? PreviewDocument) is null ? 0 : 1;
    }

    private static bool IsValidTransferPin(string? pin)
    {
        return !string.IsNullOrWhiteSpace(pin)
            && pin.Length == 6
            && pin.All(char.IsDigit);
    }

    private async Task SetTransferBusyStateAsync(bool busy, string? statusMessage)
    {
        await RunOnUiThreadAsync(() =>
        {
            IsBusy = busy;
            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                StatusMessage = statusMessage;
            }
        });
    }

    private Task UpdateStatusOnUiAsync(string message)
    {
        return RunOnUiThreadAsync(() => StatusMessage = message);
    }

    private async Task HandleTransferOperationFailureAsync(Exception ex, TransferOperationPhase phase, bool isLoadOperation)
    {
        _ = isLoadOperation;
        if (ex is UnauthorizedAccessException)
        {
            await UpdateStatusOnUiAsync(_localization["StatusTransferWrongPin"]);
            await RunOnUiThreadAsync(ShowTransferPinErrorDialog);
            return;
        }

        if (ex is InvalidOperationException && phase == TransferOperationPhase.Pin)
        {
            await RunOnUiThreadAsync(() => ShowLocalizedInfo(_localization["TransferPinRequired"]));
            return;
        }

        await RunOnUiThreadAsync(() => _errorHandler.Show(ex));
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
    private static List<DocumentEntry> ResolveEntriesByIds(IReadOnlyList<DocumentEntry> source, IEnumerable<Guid> ids)
    {
        Dictionary<Guid, DocumentEntry> map = source.ToDictionary(static x => x.Id, static x => x);
        List<DocumentEntry> result = new();

        foreach (Guid id in ids)
        {
            if (map.TryGetValue(id, out DocumentEntry? entry))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private async Task<List<string>> MaterializeTempFilesAsync(IReadOnlyList<DocumentEntry> entries, string tempDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(tempDirectory);
        List<string> files = new(entries.Count);
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        foreach (DocumentEntry entry in entries)
        {
            byte[] plain = await _hiddenStorageService.LoadAsync(entry.StoredFileName, ct);
            using SecureBufferScope secure = new(plain);

            string baseName = SanitizeFileName(entry.DisplayName);
            string extension = string.IsNullOrWhiteSpace(entry.OriginalExtension) ? ".bin" : entry.OriginalExtension;
            if (!extension.StartsWith(".", StringComparison.Ordinal))
            {
                extension = ".bin";
            }

            string path = BuildUniqueFilePath(tempDirectory, baseName, extension, used);
            await File.WriteAllBytesAsync(path, secure.Buffer, ct);
            files.Add(path);
        }

        return files;
    }

    private string? PromptForPin()
    {
        PinCodeDialog pinDialog = new()
        {
            Owner = Application.Current.MainWindow,
        };

        if (pinDialog.ShowDialog() != true)
        {
            return null;
        }

        string pin = pinDialog.Pin;
        if (!IsValidTransferPin(pin))
        {
            ShowLocalizedInfo(_localization["TransferPinRequired"]);
            return null;
        }

        return pin;
    }

    private static bool IsSstrans2Archive(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < Sstrans2Magic.Length)
        {
            return false;
        }

        byte[] header = new byte[Sstrans2Magic.Length];
        int read = stream.Read(header, 0, header.Length);
        if (read != header.Length)
        {
            return false;
        }

        bool result = header.AsSpan().SequenceEqual(Sstrans2Magic);
        Array.Clear(header, 0, header.Length);
        return result;
    }

    private static string ResolveBatchExportExtension(string originalExtension)
    {
        string extension = string.IsNullOrWhiteSpace(originalExtension)
            ? string.Empty
            : originalExtension.Trim();

        if (!extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = string.Empty;
        }

        return extension.ToLowerInvariant() switch
        {
            ".jpg" => ".jpg",
            ".jpeg" => ".jpeg",
            _ => ".png",
        };
    }

    private static string BuildBatchExportPath(string outputDirectory, string displayName, string timestamp, string extension, HashSet<string> usedPaths)
    {
        string baseName = SanitizeFileName(displayName);
        string stem = $"{baseName}_{timestamp}";

        for (int index = 0; index < 10000; index++)
        {
            string candidateFile = index == 0
                ? $"{stem}{extension}"
                : $"{stem}_{index}{extension}";
            string path = Path.Combine(outputDirectory, candidateFile);
            if (usedPaths.Add(path) && !File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(outputDirectory, $"{Guid.NewGuid():N}{extension}");
    }

    private string BuildDefaultTransferOutputDirectory()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        string outputDirectory = Path.Combine(baseDirectory, "SafeSeal", "Transfer", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
    }

    private static string ResolveExtensionFromMime(string mimeType)
    {
        return mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            _ => ".bin",
        };
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "document";
        }

        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        StringBuilder sb = new(name.Length);
        foreach (char ch in name.Trim())
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }

        string sanitized = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }

    private static string BuildUniqueFilePath(string directory, string baseName, string extension)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        return BuildUniqueFilePath(directory, baseName, extension, used);
    }

    private static string BuildUniqueFilePath(string directory, string baseName, string extension, HashSet<string> used)
    {
        for (int index = 0; index < 10000; index++)
        {
            string fileName = index == 0 ? $"{baseName}{extension}" : $"{baseName}_{index}{extension}";
            string path = Path.Combine(directory, fileName);
            if (used.Add(path) && !File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private void ShowLocalizedInfo(string message)
    {
        FluentMessageDialog dialog = new(
            _localization["ErrorTitle"],
            message,
            _localization["Close"])
        {
            Owner = Application.Current.MainWindow,
        };

        dialog.ShowDialog();
    }

    private void ShowTransferPinErrorDialog()
    {
        FluentMessageDialog dialog = new(
            _localization["TransferWrongPinTitle"],
            _localization["TransferWrongPinMessage"],
            _localization["Close"])
        {
            Owner = Application.Current.MainWindow,
        };

        dialog.ShowDialog();
    }

    private void ClearBatchSelectionState()
    {
        foreach (DocumentItemViewModel item in Documents)
        {
            item.IsBatchSelected = false;
        }

        OnPropertyChanged(nameof(SelectedForBulkText));
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(IsBatchExportVisible));
        OnPropertyChanged(nameof(IsBatchDeleteVisible));
        OnPropertyChanged(nameof(CanBatchExport));
        OnPropertyChanged(nameof(CanBatchDelete));
        OnPropertyChanged(nameof(CanCreateTransfer));
    }

    private string ResolveTransferImportName(string sourceNameOrPath)
    {
        string candidate = Path.GetFileNameWithoutExtension(sourceNameOrPath) ?? string.Empty;
        candidate = candidate.Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? string.Format(CultureInfo.CurrentCulture, _localization["TransferExtractedFileNameFormat"], DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture))
            : candidate;
    }

    private static string ResolveTransferImportExtension(string sourceNameOrPath, string mimeType)
    {
        string extension = Path.GetExtension(sourceNameOrPath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        return ResolveExtensionFromMime(mimeType);
    }

    private void AppendValidityLine(List<string> lines)
    {
        AppendValidityLine(lines, null);
    }

    private void AppendValidityLine(List<string> lines, WorkspacePreferences? workspace)
    {
        string? validityLine = BuildValidityLine(workspace);
        if (string.IsNullOrWhiteSpace(validityLine))
        {
            return;
        }

        while (lines.Count >= 5)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(validityLine);
    }

    private string? BuildValidityLine(WorkspacePreferences? workspace = null)
    {
        WatermarkValidityMode mode = workspace is null ? ValidityMode : ParseValidityMode(workspace.ValidityMode);
        WatermarkDatePreset selectedDatePreset = workspace is null ? DatePreset : ParseDatePreset(workspace.DatePreset);
        WatermarkDatePreset selectedExpiryPreset = workspace is null ? ExpiryPreset : ParseDatePreset(workspace.ExpiryPreset);
        DateTime? selectedCustomDate = workspace?.CustomDate ?? CustomDate;
        DateTime? selectedCustomExpiryDate = workspace?.CustomExpiryDate ?? CustomExpiryDate;
        WatermarkDateDisplayFormat selectedDateDisplayFormat = workspace is null
            ? DateDisplayFormat
            : ParseDateDisplayFormat(workspace.DateDisplayFormat);

        return mode switch
        {
            WatermarkValidityMode.None => null,
            WatermarkValidityMode.Date => string.Format(CultureInfo.CurrentCulture, _localization["ValidityDateLineFormat"], ResolveDateText(selectedDatePreset, selectedCustomDate, selectedDateDisplayFormat)),
            WatermarkValidityMode.ExpiryDate => string.Format(CultureInfo.CurrentCulture, _localization["ValidityExpiryLineFormat"], ResolveDateText(selectedExpiryPreset, selectedCustomExpiryDate, selectedDateDisplayFormat)),
            _ => null,
        };
    }

    private static string ResolveDateText(WatermarkDatePreset preset, DateTime? customDate, WatermarkDateDisplayFormat format)
    {
        DateTime today = DateTime.Today;
        DateTime date = preset switch
        {
            WatermarkDatePreset.Today => today,
            WatermarkDatePreset.ThisWeek => today.AddDays(7),
            WatermarkDatePreset.ThisMonth => new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            WatermarkDatePreset.Custom => customDate ?? today,
            _ => today,
        };

        return FormatDateByDisplayFormat(date, format);
    }

    private static string FormatDateByDisplayFormat(DateTime date, WatermarkDateDisplayFormat format)
    {
        return format switch
        {
            WatermarkDateDisplayFormat.EnglishShortMonth => date.ToString("yyyy MMM.d", CultureInfo.InvariantCulture),
            WatermarkDateDisplayFormat.Slash => date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            _ => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    private void RebuildTintOptions(bool refreshPreview)
    {
        string selectedKey = SelectedTintOption?.Key ?? _workspaceState.GetSnapshot().TintKey;

        TintOptions.Clear();
        TintOptions.Add(new WatermarkTintOption("blue", _localization["TintBlue"], Color.FromRgb(0x25, 0x63, 0xEB)));
        TintOptions.Add(new WatermarkTintOption("slate", _localization["TintSlate"], Color.FromRgb(0x33, 0x48, 0x55)));
        TintOptions.Add(new WatermarkTintOption("crimson", _localization["TintCrimson"], Color.FromRgb(0xB9, 0x1C, 0x1C)));
        TintOptions.Add(new WatermarkTintOption("forest", _localization["TintForest"], Color.FromRgb(0x16, 0x6A, 0x53)));

        SelectedTintOption = TintOptions.FirstOrDefault(x => string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase)) ?? TintOptions[0];

        if (refreshPreview)
        {
            _ = RefreshPreviewAsync(PreviewDocument);
        }
    }
}
