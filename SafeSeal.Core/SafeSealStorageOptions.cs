using System.IO;

namespace SafeSeal.Core;

public sealed class SafeSealStorageOptions
{
    public SafeSealStorageOptions(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory cannot be null or whitespace.", nameof(rootDirectory));
        }

        RootDirectory = rootDirectory;
    }

    public string RootDirectory { get; }

    public string VaultDirectory => Path.Combine(RootDirectory, "vault");

    public string InternalDirectory => Path.Combine(RootDirectory, "Internal");

    public string CatalogPath => Path.Combine(RootDirectory, "catalog.db");

    public string LegacyDirectory => Path.Combine(RootDirectory, "legacy");

    public string MigrationSentinelPath => Path.Combine(RootDirectory, "migration.v1.done");

    public static SafeSealStorageOptions CreateDefault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SafeSealStorageOptions(Path.Combine(localAppData, "SafeSeal"));
    }
}