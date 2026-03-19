using System.IO;
using System.Text.RegularExpressions;

namespace SafeSeal.Core;

public sealed class HiddenVaultStorageService
{
    private static readonly Regex StoredFileNamePattern = new(
        "^(?:[a-f0-9]{32}|[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})\\.seal$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    public bool Exists(string storedFileName)
    {
        string vaultPath = GetSafeStoredPath(_options.VaultDirectory, storedFileName);
        if (File.Exists(vaultPath))
        {
            return true;
        }

        string internalPath = GetSafeStoredPath(_options.InternalDirectory, storedFileName);
        return File.Exists(internalPath);
    }

    public Task SaveAsync(Guid id, byte[] plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            string storedFileName = GetStoredFileName(id);
            string targetPath = GetSafeStoredPath(_options.VaultDirectory, storedFileName);
            string internalPath = GetSafeStoredPath(_options.InternalDirectory, storedFileName);

            // Guard against stale files from previous crashes.
            DeleteIfExists(targetPath);
            DeleteIfExists(internalPath);

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

            string vaultPath = GetSafeStoredPath(_options.VaultDirectory, storedFileName);
            string internalPath = GetSafeStoredPath(_options.InternalDirectory, storedFileName);

            DeleteIfExists(vaultPath);
            DeleteIfExists(internalPath);
        }, ct);
    }

    public Task CleanupOrphanedAsync(IReadOnlySet<string> knownStoredFiles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(knownStoredFiles);

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var validKnown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in knownStoredFiles)
            {
                if (IsValidStoredFileName(file))
                {
                    validKnown.Add(file);
                }
            }

            CleanupDirectory(_options.VaultDirectory, validKnown, ct);
            CleanupDirectory(_options.InternalDirectory, validKnown, ct);
        }, ct);
    }

    private string? ResolveExistingPath(string storedFileName)
    {
        string vaultPath = GetSafeStoredPath(_options.VaultDirectory, storedFileName);
        if (File.Exists(vaultPath))
        {
            return vaultPath;
        }

        string internalPath = GetSafeStoredPath(_options.InternalDirectory, storedFileName);
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
            if (!IsValidStoredFileName(fileName))
            {
                DeleteIfExists(file);
                continue;
            }

            if (knownStoredFiles.Contains(fileName))
            {
                continue;
            }

            DeleteIfExists(file);
        }
    }

    private static bool IsValidStoredFileName(string storedFileName)
    {
        return !string.IsNullOrWhiteSpace(storedFileName) && StoredFileNamePattern.IsMatch(storedFileName);
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

    private static string GetSafeStoredPath(string baseDirectory, string storedFileName)
    {
        if (!IsValidStoredFileName(storedFileName))
        {
            throw new InvalidOperationException("Invalid stored file name detected.");
        }

        string normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(baseDirectory));
        string candidate = Path.Combine(baseDirectory, storedFileName);
        string canonicalPath = Path.GetFullPath(candidate);

        if (!canonicalPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path traversal attempt detected.");
        }

        return canonicalPath;
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
