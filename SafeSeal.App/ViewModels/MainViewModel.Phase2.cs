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

public partial class MainViewModel
{
    private static readonly byte[] Sstrans2Magic = "SSTRANS2"u8.ToArray();

    [ObservableProperty]
    private WatermarkValidityMode validityMode = WatermarkValidityMode.None;

    [ObservableProperty]
    private WatermarkDatePreset datePreset = WatermarkDatePreset.Today;

    [ObservableProperty]
    private WatermarkDatePreset expiryPreset = WatermarkDatePreset.Today;

    [ObservableProperty]
    private string customDateText = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        [ObservableProperty]
    private string customExpiryDateText = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private bool isBatchModeEnabled;

    public string BatchProcessText => _localization["BatchProcess"];

    public string BatchModeButtonText => IsBatchModeEnabled
        ? _localization["BatchModeDisable"]
        : _localization["BatchModeEnable"];

    public string RunBatchText => _localization["RunBatch"];

    public bool IsBatchRunVisible => IsBatchModeEnabled;

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
    partial void OnIsBatchModeEnabledChanged(bool value)
    {
        ClearBatchSelectionState();

        if (value)
        {
            _previewCts?.Cancel();
            _handlingBatchSelection = true;
            try
            {
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

        OnPropertyChanged(nameof(BatchModeButtonText));
        OnPropertyChanged(nameof(IsBatchRunVisible));
        OnPropertyChanged(nameof(SelectedForBulkText));
    }

    partial void OnValidityModeChanged(WatermarkValidityMode value)
    {
        OnPropertyChanged(nameof(IsValidityNone));
        OnPropertyChanged(nameof(IsValidityDate));
        OnPropertyChanged(nameof(IsValidityExpiryDate));
        OnPropertyChanged(nameof(IsDateOptionsVisible));
        OnPropertyChanged(nameof(IsExpiryOptionsVisible));
        OnPropertyChanged(nameof(IsDateCustomVisible));
        OnPropertyChanged(nameof(IsExpiryCustomVisible));
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnDatePresetChanged(WatermarkDatePreset value)
    {
        OnPropertyChanged(nameof(IsDatePresetToday));
        OnPropertyChanged(nameof(IsDatePresetCustom));
        OnPropertyChanged(nameof(IsDateCustomVisible));
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnExpiryPresetChanged(WatermarkDatePreset value)
    {
        OnPropertyChanged(nameof(IsExpiryPresetToday));
        OnPropertyChanged(nameof(IsExpiryPresetThisWeek));
        OnPropertyChanged(nameof(IsExpiryPresetThisMonth));
        OnPropertyChanged(nameof(IsExpiryPresetCustom));
        OnPropertyChanged(nameof(IsExpiryCustomVisible));
        _ = RefreshPreviewAsync(SelectedDocument);
    }

    partial void OnCustomDateTextChanged(string value)
    {
        if (DatePreset == WatermarkDatePreset.Custom)
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }

    partial void OnCustomExpiryDateTextChanged(string value)
    {
        if (ExpiryPreset == WatermarkDatePreset.Custom)
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }

        [RelayCommand]
    private void ToggleBatchMode()
    {
        IsBatchModeEnabled = !IsBatchModeEnabled;
    }

    [RelayCommand]
    private async Task RunBatchProcessAsync()
    {
        await BatchProcessAsync();
    }

    [RelayCommand]
    private async Task BatchProcessAsync()
    {
        if (!IsBatchModeEnabled)
        {
            ShowLocalizedInfo(_localization["BatchModeEnableHint"]);
            return;
        }

        IReadOnlyList<DocumentItemViewModel> selectedItems = Documents.Where(static x => x.IsBatchSelected).ToList();
        if (selectedItems.Count == 0)
        {
            ShowLocalizedInfo(_localization["StatusSelectDocuments"]);
            return;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "SafeSeal", "batch", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            IsBusy = true;
            StatusMessage = _localization["StatusBatchPreparing"];

            IReadOnlyList<DocumentEntry> allEntries = await _documentVaultService.ListAsync(CancellationToken.None);
            List<DocumentEntry> selectedEntries = ResolveEntriesByIds(allEntries, selectedItems.Select(static x => x.Id));
            List<string> tempFiles = await MaterializeTempFilesAsync(selectedEntries, tempDirectory, CancellationToken.None);

            string outputDirectory = BuildDefaultBatchOutputDirectory();
            IProgress<BatchProgress> progress = new Progress<BatchProgress>(p =>
                StatusMessage = string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchProgressFormat"], p.Completed, p.Total));

            BatchResult result = await _batchWatermarkService.ExportAsync(tempFiles, BuildWatermarkOptions(), outputDirectory, progress, CancellationToken.None);

            StatusMessage = result.Errors.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchCompletedFormat"], result.OutputFiles.Count, outputDirectory)
                : string.Format(CultureInfo.CurrentCulture, _localization["StatusBatchCompletedWithErrorsFormat"], result.OutputFiles.Count, result.Errors.Count, outputDirectory);
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateArchiveAsync()
    {
        if (!IsBatchModeEnabled)
        {
            ShowLocalizedInfo(_localization["BatchModeEnableHint"]);
            return;
        }

        IReadOnlyList<DocumentItemViewModel> selectedItems = Documents.Where(static x => x.IsBatchSelected).ToList();
        if (selectedItems.Count == 0)
        {
            ShowLocalizedInfo(_localization["StatusSelectDocuments"]);
            return;
        }

        string? pin = PromptForPin();
        if (pin is null)
        {
            return;
        }

        IReadOnlyList<DocumentEntry> allEntries = await _documentVaultService.ListAsync(CancellationToken.None);
        List<DocumentEntry> selectedEntries = ResolveEntriesByIds(allEntries, selectedItems.Select(static x => x.Id));

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        string defaultName = selectedEntries.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, _localization["TransferDefaultSingleArchiveFormat"], SanitizeFileName(selectedEntries[0].DisplayName), timestamp)
            : string.Format(CultureInfo.CurrentCulture, _localization["TransferDefaultMultiArchiveFormat"], timestamp);

                bool isMultiPackage = selectedEntries.Count > 1;
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
            return;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "SafeSeal", "transfer", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        try
        {
            IsBusy = true;
            StatusMessage = _localization["StatusTransferPreparing"];

            if (selectedEntries.Count == 1)
            {
                await _safeTransferService.CreateArchiveAsync(selectedEntries[0], saveDialog.FileName, pin, CancellationToken.None);
            }
            else
            {
                List<string> tempFiles = await MaterializeTempFilesAsync(selectedEntries, tempDirectory, CancellationToken.None);
                IProgress<BatchProgress> progress = new Progress<BatchProgress>(p =>
                    StatusMessage = string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferProgressFormat"], p.Completed, p.Total));

                await _transferPackageService.CreateMergedPackageAsync(tempFiles, pin, saveDialog.FileName, progress, CancellationToken.None);
            }

            StatusMessage = string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferCreateSuccess"], saveDialog.FileName);
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadArchiveAsync()
    {
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

        string? pin = PromptForPin();
        if (pin is null)
        {
            return;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "SafeSeal", "transfer-import", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        string tempExtractDirectory = Path.Combine(tempDirectory, "extract");
        List<DocumentEntry> importedEntries = [];
        int extractedErrorCount = 0;

        try
        {
            IsBusy = true;
            StatusMessage = _localization["StatusTransferPreparing"];

            if (_safeTransferService.CanReadFormat(openDialog.FileName))
            {
                TransferArchiveContent content = await _safeTransferService.ExtractArchiveAsync(openDialog.FileName, pin, CancellationToken.None);
                try
                {
                    Directory.CreateDirectory(tempDirectory);

                    string importName = ResolveTransferImportName(content.OriginalFileName);
                    string extension = ResolveTransferImportExtension(content.OriginalFileName, content.MimeType);
                    string tempPath = BuildUniqueFilePath(tempDirectory, SanitizeFileName(importName), extension);

                    await File.WriteAllBytesAsync(tempPath, content.ImageData, CancellationToken.None);

                    DocumentEntry imported = await _documentVaultService.ImportAsync(
                        tempPath,
                        importName,
                        NameConflictBehavior.AutoSuffix,
                        CancellationToken.None);

                    importedEntries.Add(imported);
                }
                finally
                {
                    Array.Clear(content.ImageData, 0, content.ImageData.Length);
                }
            }
            else if (IsSstrans2Archive(openDialog.FileName))
            {
                Directory.CreateDirectory(tempExtractDirectory);

                IProgress<BatchProgress> progress = new Progress<BatchProgress>(p =>
                    StatusMessage = string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferProgressFormat"], p.Completed, p.Total));

                BatchResult result = await _transferPackageService.ExtractMergedPackageAsync(
                    openDialog.FileName,
                    pin,
                    tempExtractDirectory,
                    progress,
                    CancellationToken.None);

                extractedErrorCount += result.Errors.Count;

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
                    }
                    catch
                    {
                        extractedErrorCount++;
                    }
                }
            }
            else
            {
                ShowLocalizedInfo(_localization["ErrorTransferUnknownFormat"]);
                return;
            }

            if (importedEntries.Count > 0)
            {
                DocumentEntry lastImported = importedEntries[^1];
                await ReloadDocumentsAsync(lastImported.Id);

                StatusMessage = extractedErrorCount == 0
                    ? string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferImportVaultSuccessFormat"], importedEntries.Count)
                    : string.Format(CultureInfo.CurrentCulture, _localization["StatusTransferImportVaultPartialFormat"], importedEntries.Count, extractedErrorCount);
            }
            else
            {
                StatusMessage = _localization["StatusTransferImportVaultNone"];
            }
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = _localization["StatusTransferWrongPin"];
            ShowTransferPinErrorDialog();
        }
        catch (Exception ex)
        {
            _errorHandler.Show(ex);
        }
        finally
        {
            SafeDeleteDirectory(tempDirectory);
            IsBusy = false;
        }
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
        NicknameDialog pinDialog = new(_localization["TransferPinTitle"], _localization["TransferPinLabel"], string.Empty)
        {
            Owner = Application.Current.MainWindow,
        };

        if (pinDialog.ShowDialog() != true)
        {
            return null;
        }

        string pin = pinDialog.Nickname;
        if (pin.Length != 6 || pin.Any(static c => !char.IsDigit(c)))
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

    private string BuildDefaultBatchOutputDirectory()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        string outputDirectory = Path.Combine(baseDirectory, "SafeSeal", "Batch", DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
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
        string? validityLine = BuildValidityLine();
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

    private string? BuildValidityLine()
    {
        return ValidityMode switch
        {
            WatermarkValidityMode.None => null,
            WatermarkValidityMode.Date => string.Format(CultureInfo.CurrentCulture, _localization["ValidityDateLineFormat"], ResolveDateText(DatePreset, CustomDateText)),
            WatermarkValidityMode.ExpiryDate => string.Format(CultureInfo.CurrentCulture, _localization["ValidityExpiryLineFormat"], ResolveDateText(ExpiryPreset, CustomExpiryDateText)),
            _ => null,
        };
    }

    private string ResolveDateText(WatermarkDatePreset preset, string customInput)
    {
        DateTime today = DateTime.Today;
        DateTime date = preset switch
        {
            WatermarkDatePreset.Today => today,
            WatermarkDatePreset.ThisWeek => today.AddDays(7),
            WatermarkDatePreset.ThisMonth => new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            WatermarkDatePreset.Custom => ParseCustomDate(customInput, today),
            _ => today,
        };

        return FormatLocalizedDate(date);
    }

    private static DateTime ParseCustomDate(string input, DateTime fallback)
    {
        string text = input?.Trim() ?? string.Empty;
        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : fallback;
    }

    private string FormatLocalizedDate(DateTime date)
    {
        if (_localization.CurrentLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        }

        if (_localization.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return date.ToString("d", CultureInfo.CurrentCulture);
    }

    private void RebuildTintOptions(bool refreshPreview)
    {
        Color selected = SelectedTintOption?.Color ?? Color.FromRgb(0x25, 0x63, 0xEB);

        TintOptions.Clear();
        TintOptions.Add(new WatermarkTintOption(_localization["TintBlue"], Color.FromRgb(0x25, 0x63, 0xEB)));
        TintOptions.Add(new WatermarkTintOption(_localization["TintSlate"], Color.FromRgb(0x33, 0x48, 0x55)));
        TintOptions.Add(new WatermarkTintOption(_localization["TintCrimson"], Color.FromRgb(0xB9, 0x1C, 0x1C)));
        TintOptions.Add(new WatermarkTintOption(_localization["TintForest"], Color.FromRgb(0x16, 0x6A, 0x53)));

        SelectedTintOption = TintOptions.FirstOrDefault(x => x.Color.Equals(selected)) ?? TintOptions[0];

        if (refreshPreview)
        {
            _ = RefreshPreviewAsync(SelectedDocument);
        }
    }
}








