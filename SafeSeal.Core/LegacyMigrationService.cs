using System.IO;
using System.Globalization;
using System.Text;

namespace SafeSeal.Core;

public sealed class LegacyMigrationService
{
    private readonly SafeSealStorageOptions _options;
    private readonly HiddenVaultStorageService _storage;
    private readonly IDocumentCatalogService _catalog;

    public LegacyMigrationService(
        SafeSealStorageOptions options,
        HiddenVaultStorageService storage,
        IDocumentCatalogService catalog)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task RunIfNeededAsync(CancellationToken ct)
    {
        if (File.Exists(_options.MigrationSentinelPath))
        {
            return;
        }

        Directory.CreateDirectory(_options.RootDirectory);

        if (Directory.Exists(_options.LegacyDirectory))
        {
            string[] legacyFiles = Directory.GetFiles(_options.LegacyDirectory, "*.seal", SearchOption.TopDirectoryOnly);
            foreach (string legacyFile in legacyFiles)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    byte[] plaintext = VaultManager.LoadSecurely(legacyFile);
                    using SecureBufferScope secure = new(plaintext);

                    string initialName = Path.GetFileNameWithoutExtension(legacyFile);
                    string displayName = await ResolveUniqueDisplayNameAsync(initialName, ct);

                    Guid id = Guid.NewGuid();
                    await _storage.SaveAsync(id, secure.Buffer, ct);

                    DateTime now = DateTime.UtcNow;
                    DocumentEntry entry = new(
                        id,
                        displayName,
                        _storage.GetStoredFileName(id),
                        ".unknown",
                        now,
                        now);

                    await _catalog.UpsertAsync(entry, ct);
                }
                catch
                {
                    // Best-effort migration: skip unreadable or invalid legacy files.
                }
            }
        }

        File.WriteAllText(_options.MigrationSentinelPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), Encoding.UTF8);
    }

    private async Task<string> ResolveUniqueDisplayNameAsync(string baseName, CancellationToken ct)
    {
        string trimmedBase = string.IsNullOrWhiteSpace(baseName) ? "Migrated Document" : baseName.Trim().Normalize(NormalizationForm.FormC);
        string candidate = trimmedBase;
        int suffix = 2;

        while (await _catalog.FindByNameAsync(candidate, ct) is not null)
        {
            candidate = $"{trimmedBase} ({suffix})";
            suffix++;
        }

        return candidate;
    }
}

