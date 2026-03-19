using System.IO;

namespace SafeSeal.Core;

public sealed class HiddenVaultStorageService
{
    private readonly SafeSealStorageOptions _options;

    public HiddenVaultStorageService(SafeSealStorageOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        Directory.CreateDirectory(_options.RootDirectory);
        Directory.CreateDirectory(_options.VaultDirectory);
        Directory.CreateDirectory(_options.InternalDirectory);

        TrySetHidden(_options.VaultDirectory);
        TrySetHidden(_options.InternalDirectory);
    }

    public string GetStoredFileName(Guid id)
    {
        return $"{id:N}.seal";
    }

    public Task SaveAsync(Guid id, byte[] plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            string storedFileName = GetStoredFileName(id);
            string targetPath = GetStoredPath(storedFileName);

            // Guard against stale files from previous crashes.
            DeleteIfExists(targetPath);
            DeleteIfExists(Path.Combine(_options.InternalDirectory, storedFileName));

            VaultManager.Save(plaintext, targetPath);
            TrySetHidden(targetPath);
        }, ct);
    }

    public Task<byte[]> LoadAsync(string storedFileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            throw new ArgumentException("Stored file name cannot be null or whitespace.", nameof(storedFileName));
        }

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            string? path = ResolveExistingPath(storedFileName);
            if (path is null)
            {
                throw new FileNotFoundException("Internal vault file was not found.", storedFileName);
            }

            return VaultManager.LoadSecurely(path);
        }, ct);
    }

    public Task DeleteAsync(string storedFileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            DeleteIfExists(Path.Combine(_options.VaultDirectory, storedFileName));
            DeleteIfExists(Path.Combine(_options.InternalDirectory, storedFileName));
        }, ct);
    }

    public Task CleanupOrphanedAsync(IReadOnlySet<string> knownStoredFiles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(knownStoredFiles);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            CleanupDirectory(_options.VaultDirectory, knownStoredFiles, ct);
            CleanupDirectory(_options.InternalDirectory, knownStoredFiles, ct);
        }, ct);
    }

    private string GetStoredPath(string storedFileName)
    {
        return Path.Combine(_options.VaultDirectory, storedFileName);
    }

    private string? ResolveExistingPath(string storedFileName)
    {
        string vaultPath = Path.Combine(_options.VaultDirectory, storedFileName);
        if (File.Exists(vaultPath))
        {
            return vaultPath;
        }

        string internalPath = Path.Combine(_options.InternalDirectory, storedFileName);
        if (File.Exists(internalPath))
        {
            return internalPath;
        }

        return null;
    }

    private static void CleanupDirectory(string directoryPath, IReadOnlySet<string> knownStoredFiles, CancellationToken ct)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        string[] files = Directory.GetFiles(directoryPath, "*.seal", SearchOption.TopDirectoryOnly);
        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(file);
            if (knownStoredFiles.Contains(fileName))
            {
                continue;
            }

            DeleteIfExists(file);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static void TrySetHidden(string path)
    {
        try
        {
            FileAttributes existing = File.GetAttributes(path);
            if ((existing & FileAttributes.Hidden) == 0)
            {
                File.SetAttributes(path, existing | FileAttributes.Hidden);
            }
        }
        catch
        {
            // Best-effort attribute hardening.
        }
    }
}