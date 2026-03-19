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

        TrySetHidden(_options.VaultDirectory);
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

            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
            }

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

            string path = GetStoredPath(storedFileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Internal vault file was not found.", path);
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

            string path = GetStoredPath(storedFileName);
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }, ct);
    }

    private string GetStoredPath(string storedFileName)
    {
        return Path.Combine(_options.VaultDirectory, storedFileName);
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
