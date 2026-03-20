using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace SafeSeal.Core;

public sealed class DocumentVaultService : IDocumentVaultService
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".tif",
        ".tiff",
    };

    private static readonly HashSet<string> AllowedExportExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
    };

    private const string SignatureSalt = "SafeSealV1";

    private readonly SafeSealStorageOptions _options;
    private readonly IDocumentCatalogService _catalog;
    private readonly HiddenVaultStorageService _storage;
    private readonly WatermarkRenderer _watermarkRenderer;
    private readonly ExportService _exportService;
    private readonly LegacyMigrationService _legacyMigration;
    private readonly IDeleteOperationLog _deleteLog;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public DocumentVaultService()
        : this(SafeSealStorageOptions.CreateDefault())
    {
    }

    public DocumentVaultService(SafeSealStorageOptions options)
        : this(
            options,
            new DocumentCatalogService(options),
            new HiddenVaultStorageService(options),
            new WatermarkRenderer(),
            new ExportService(),
            new TraceDeleteOperationLog())
    {
    }

    public DocumentVaultService(
        SafeSealStorageOptions options,
        IDocumentCatalogService catalog,
        HiddenVaultStorageService storage,
        WatermarkRenderer watermarkRenderer,
        ExportService exportService,
        IDeleteOperationLog deleteOperationLog)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _watermarkRenderer = watermarkRenderer ?? throw new ArgumentNullException(nameof(watermarkRenderer));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _legacyMigration = new LegacyMigrationService(_options, _storage, _catalog);
        _deleteLog = deleteOperationLog ?? throw new ArgumentNullException(nameof(deleteOperationLog));
    }

    public async Task<IReadOnlyList<DocumentEntry>> ListAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        return await _catalog.GetAllAsync(ct);
    }

    public async Task<DocumentEntry> ImportAsync(
        string sourceImagePath,
        string displayName,
        NameConflictBehavior behavior,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            throw new ArgumentException("Source image path cannot be null or whitespace.", nameof(sourceImagePath));
        }

        if (!File.Exists(sourceImagePath))
        {
            throw new FileNotFoundException("Source image file was not found.", sourceImagePath);
        }

        string normalizedName = NormalizeName(displayName);
        string extension = Path.GetExtension(sourceImagePath);

        if (!AllowedImageExtensions.Contains(extension))
        {
            throw new InvalidDataException("Only image files can be imported into the vault.");
        }

        DocumentEntry? existingByName = await _catalog.FindByNameAsync(normalizedName, ct);

        Guid id;
        DateTime createdUtc;
        bool createsNewStorage;

        if (existingByName is not null)
        {
            switch (behavior)
            {
                case NameConflictBehavior.Reject:
                    throw new InvalidOperationException("A document with this name already exists.");

                case NameConflictBehavior.AutoSuffix:
                    normalizedName = await ResolveAutoSuffixAsync(normalizedName, ct);
                    id = Guid.NewGuid();
                    createdUtc = DateTime.UtcNow;
                    createsNewStorage = true;
                    break;

                case NameConflictBehavior.AskOverwrite:
                default:
                    id = existingByName.Id;
                    createdUtc = existingByName.CreatedUtc;
                    createsNewStorage = false;
                    break;
            }
        }
        else
        {
            id = Guid.NewGuid();
            createdUtc = DateTime.UtcNow;
            createsNewStorage = true;
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(sourceImagePath, ct);
        using SecureBufferScope secure = new(fileBytes);

        string storedFileName = _storage.GetStoredFileName(id);

        try
        {
            await _storage.SaveAsync(id, secure.Buffer, ct);

            DateTime updatedUtc = DateTime.UtcNow;
            DocumentEntry entry = new(
                id,
                normalizedName,
                storedFileName,
                extension,
                createdUtc,
                updatedUtc);

            await _catalog.UpsertAsync(entry, ct);
            return entry;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            if (createsNewStorage)
            {
                await SafeDeleteStoredFileAsync(storedFileName, ct);
            }

            throw new InvalidOperationException("A document with this name already exists or collides with an existing record.", ex);
        }
        catch (IOException)
        {
            if (createsNewStorage)
            {
                await SafeDeleteStoredFileAsync(storedFileName, ct);
            }

            throw;
        }
        catch
        {
            if (createsNewStorage)
            {
                await SafeDeleteStoredFileAsync(storedFileName, ct);
            }

            throw;
        }
    }

    public async Task<BitmapSource> BuildPreviewAsync(Guid documentId, WatermarkOptions options, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        DocumentEntry entry = await GetRequiredEntryByIdAsync(documentId, ct).ConfigureAwait(false);
        WatermarkOptions signedOptions = BuildSignedOptions(entry, options);

        return await RenderPreviewAsync(entry, signedOptions, ct).ConfigureAwait(false);
    }

    public async Task ExportAsync(Guid documentId, WatermarkOptions options, string outputPath, int jpegQuality, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(outputPath));
        }

        string extension = Path.GetExtension(outputPath);
        if (!AllowedExportExtensions.Contains(extension))
        {
            throw new ArgumentException($"Unsupported export format: {extension}", nameof(outputPath));
        }

        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        DocumentEntry entry = await GetRequiredEntryByIdAsync(documentId, ct).ConfigureAwait(false);

        WatermarkOptions signedOptions = BuildSignedOptions(entry, options);
        BitmapSource preview = await RenderPreviewAsync(entry, signedOptions, ct).ConfigureAwait(false);

        DateTime exportUtc = DateTime.UtcNow;
        ExportMetadataContext metadataContext = new(
            signedOptions.SignatureId ?? string.Empty,
            signedOptions.TemplateId,
            signedOptions.TemplateVersion,
            exportUtc);

        bool metadataEmbedded = string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(() => _exportService.ExportAsPng(preview, outputPath, metadataContext), ct).ConfigureAwait(false)
            : await Task.Run(() => _exportService.ExportAsJpeg(preview, outputPath, jpegQuality, metadataContext), ct).ConfigureAwait(false);

        if (!metadataEmbedded)
        {
            Trace.TraceWarning("Export completed without metadata embedding. Path={0}", outputPath);
        }
    }

    public async Task RenameAsync(Guid documentId, string newDisplayName, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        string normalizedName = NormalizeName(newDisplayName);
        DocumentEntry? existingByName = await _catalog.FindByNameAsync(normalizedName, ct);
        if (existingByName is not null && existingByName.Id != documentId)
        {
            throw new InvalidOperationException("A document with this name already exists.");
        }

        DocumentEntry existing = await GetRequiredEntryByIdAsync(documentId, ct);
        DocumentEntry updated = existing with
        {
            DisplayName = normalizedName,
            UpdatedUtc = DateTime.UtcNow,
        };

        await _catalog.UpsertAsync(updated, ct);
    }

    public async Task DeleteAsync(Guid documentId, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        DocumentEntry existing = await GetRequiredEntryByIdAsync(documentId, ct);
        string deleteOpId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        await _catalog.MarkDeletingAsync(documentId, deleteOpId, DateTime.UtcNow, ct);
        _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "MarkDeleting", "Success", "Marked deleting", DateTime.UtcNow));

        try
        {
            await _storage.DeleteAsync(existing.StoredFileName, ct);
            _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "PhysicalDelete", "Success", "Stored file removed", DateTime.UtcNow));

            await _catalog.FinalizeDeleteAsync(documentId, deleteOpId, DateTime.UtcNow, ct);
            _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "FinalizeDelete", "Success", "Catalog finalized", DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "DeleteFlow", "Failed", SanitizeException(ex), DateTime.UtcNow));

            try
            {
                await _catalog.RecoverFromDeletingAsync(documentId, deleteOpId, ct);
                _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "RecoverFromDeleting", "Success", "Compensation completed", DateTime.UtcNow));
            }
            catch (Exception recoverEx)
            {
                _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "RecoverFromDeleting", "Failed", SanitizeException(recoverEx), DateTime.UtcNow));

                try
                {
                    await _catalog.MarkInterventionRequiredAsync(documentId, deleteOpId, SanitizeException(recoverEx), DateTime.UtcNow, ct);
                    _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "ManualInterventionRequired", "Success", "Record flagged for manual intervention", DateTime.UtcNow));
                }
                catch (Exception interventionEx)
                {
                    _deleteLog.Write(new DeleteOperationEvent(documentId, deleteOpId, "ManualInterventionRequired", "Failed", SanitizeException(interventionEx), DateTime.UtcNow));
                }
            }

            throw;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _catalog.InitializeAsync(ct);
            await _legacyMigration.RunIfNeededAsync(ct);
            await ReconcileDeletingRecordsAsync(ct);

            IReadOnlyList<DocumentEntry> entries = await _catalog.GetAllAsync(ct);
            IReadOnlyList<DeletingDocumentEntry> deletingEntries = await _catalog.GetDeletingAsync(ct);

            var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DocumentEntry entry in entries)
            {
                knownFiles.Add(entry.StoredFileName);
            }

            foreach (DeletingDocumentEntry deleting in deletingEntries)
            {
                knownFiles.Add(deleting.StoredFileName);
            }

            await _storage.CleanupOrphanedAsync(knownFiles, ct);

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task ReconcileDeletingRecordsAsync(CancellationToken ct)
    {
        IReadOnlyList<DeletingDocumentEntry> deletingEntries = await _catalog.GetDeletingAsync(ct);

        foreach (DeletingDocumentEntry deleting in deletingEntries)
        {
            ct.ThrowIfCancellationRequested();

            if (deleting.RequiresIntervention)
            {
                _deleteLog.Write(new DeleteOperationEvent(
                    deleting.Id,
                    deleting.DeleteOperationId,
                    "StartupReconcile",
                    "Skipped",
                    "Manual intervention required",
                    DateTime.UtcNow));
                continue;
            }

            bool fileExists;
            try
            {
                fileExists = _storage.Exists(deleting.StoredFileName);
            }
            catch (InvalidOperationException)
            {
                fileExists = false;
            }

            try
            {
                if (fileExists)
                {
                    await _catalog.RecoverFromDeletingAsync(deleting.Id, deleting.DeleteOperationId, ct);
                    _deleteLog.Write(new DeleteOperationEvent(deleting.Id, deleting.DeleteOperationId, "StartupReconcile", "Recovered", "Recovered to active state", DateTime.UtcNow));
                }
                else
                {
                    await _catalog.FinalizeDeleteAsync(deleting.Id, deleting.DeleteOperationId, DateTime.UtcNow, ct);
                    _deleteLog.Write(new DeleteOperationEvent(deleting.Id, deleting.DeleteOperationId, "StartupReconcile", "Finalized", "Missing file finalized as deleted", DateTime.UtcNow));
                }
            }
            catch (Exception ex)
            {
                await _catalog.MarkInterventionRequiredAsync(deleting.Id, deleting.DeleteOperationId, SanitizeException(ex), DateTime.UtcNow, ct);
                _deleteLog.Write(new DeleteOperationEvent(deleting.Id, deleting.DeleteOperationId, "StartupReconcile", "InterventionRequired", SanitizeException(ex), DateTime.UtcNow));
            }
        }
    }

    private async Task<BitmapSource> RenderPreviewAsync(DocumentEntry entry, WatermarkOptions options, CancellationToken ct)
    {
        byte[] decrypted = await _storage.LoadAsync(entry.StoredFileName, ct).ConfigureAwait(false);
        BitmapSource preview = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using SecureBufferScope secure = new(decrypted);
            return _watermarkRenderer.Render(secure.Buffer, options);
        }, ct).ConfigureAwait(false);

        return preview;
    }

    private async Task<DocumentEntry> GetRequiredEntryByIdAsync(Guid id, CancellationToken ct)
    {
        IReadOnlyList<DocumentEntry> entries = await _catalog.GetAllAsync(ct);
        foreach (DocumentEntry entry in entries)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }

        throw new FileNotFoundException("Document was not found in vault catalog.");
    }

    private async Task<string> ResolveAutoSuffixAsync(string original, CancellationToken ct)
    {
        string trimmed = original.Trim();
        string candidate = trimmed;
        int counter = 2;

        while (await _catalog.FindByNameAsync(candidate, ct) is not null)
        {
            candidate = $"{trimmed} ({counter})";
            counter++;
        }

        return candidate;
    }

    private async Task SafeDeleteStoredFileAsync(string storedFileName, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(storedFileName, ct);
        }
        catch
        {
            // Best effort: do not hide original import failure.
        }
    }

    private static WatermarkOptions BuildSignedOptions(DocumentEntry entry, WatermarkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string templateId = string.IsNullOrWhiteSpace(options.TemplateId)
            ? "custom-multi-line"
            : options.TemplateId.Trim();

        string payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Id:D}|{entry.CreatedUtc.ToUniversalTime():O}|{templateId}|{SignatureSalt}");

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] hash = SHA256.HashData(payloadBytes);
        string signatureId = Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 16);

        Array.Clear(payloadBytes, 0, payloadBytes.Length);
        Array.Clear(hash, 0, hash.Length);

        return options with
        {
            TemplateId = templateId,
            SignatureId = signatureId,
            TemplateVersion = options.TemplateVersion <= 0 ? 1 : options.TemplateVersion,
        };
    }

    
    private static string SanitizeException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.GetType().Name;
    }

    private static string NormalizeName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Document name cannot be null or whitespace.", nameof(displayName));
        }

        return displayName.Trim().Normalize(NormalizationForm.FormC);
    }
}






