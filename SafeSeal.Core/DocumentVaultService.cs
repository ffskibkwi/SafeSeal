using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

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

    private readonly SafeSealStorageOptions _options;
    private readonly IDocumentCatalogService _catalog;
    private readonly HiddenVaultStorageService _storage;
    private readonly WatermarkRenderer _watermarkRenderer;
    private readonly ExportService _exportService;
    private readonly LegacyMigrationService _legacyMigration;
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
            new ExportService())
    {
    }

    public DocumentVaultService(
        SafeSealStorageOptions options,
        IDocumentCatalogService catalog,
        HiddenVaultStorageService storage,
        WatermarkRenderer watermarkRenderer,
        ExportService exportService)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _watermarkRenderer = watermarkRenderer ?? throw new ArgumentNullException(nameof(watermarkRenderer));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _legacyMigration = new LegacyMigrationService(_options, _storage, _catalog);
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
                    break;

                case NameConflictBehavior.AskOverwrite:
                default:
                    id = existingByName.Id;
                    createdUtc = existingByName.CreatedUtc;
                    break;
            }
        }
        else
        {
            id = Guid.NewGuid();
            createdUtc = DateTime.UtcNow;
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(sourceImagePath, ct);
        using SecureBufferScope secure = new(fileBytes);

        await _storage.SaveAsync(id, secure.Buffer, ct);

        DateTime updatedUtc = DateTime.UtcNow;
        DocumentEntry entry = new(
            id,
            normalizedName,
            _storage.GetStoredFileName(id),
            extension,
            createdUtc,
            updatedUtc);

        await _catalog.UpsertAsync(entry, ct);
        return entry;
    }

    public async Task<BitmapSource> BuildPreviewAsync(Guid documentId, WatermarkOptions options, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        DocumentEntry entry = await GetRequiredEntryByIdAsync(documentId, ct).ConfigureAwait(false);

        byte[] decrypted = await _storage.LoadAsync(entry.StoredFileName, ct).ConfigureAwait(false);
        BitmapSource preview = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using SecureBufferScope secure = new(decrypted);
            return _watermarkRenderer.Render(secure.Buffer, options);
        }, ct).ConfigureAwait(false);

        return preview;
    }

    public async Task ExportAsync(Guid documentId, WatermarkOptions options, string outputPath, int jpegQuality, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or whitespace.", nameof(outputPath));
        }

        BitmapSource preview = await BuildPreviewAsync(documentId, options, ct).ConfigureAwait(false);

        string extension = Path.GetExtension(outputPath);
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => _exportService.ExportAsPng(preview, outputPath), ct).ConfigureAwait(false);
            return;
        }

        await Task.Run(() => _exportService.ExportAsJpeg(preview, outputPath, jpegQuality), ct).ConfigureAwait(false);
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

        await _storage.DeleteAsync(existing.StoredFileName, ct);
        await _catalog.SoftDeleteAsync(documentId, ct);
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

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
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

    private static string NormalizeName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Document name cannot be null or whitespace.", nameof(displayName));
        }

        return displayName.Trim().Normalize(NormalizationForm.FormC);
    }
}


