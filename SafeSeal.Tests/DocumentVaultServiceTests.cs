using System.Security.Cryptography;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class DocumentVaultServiceTests : IDisposable
{
    private readonly string _root;
    private readonly SafeSealStorageOptions _options;

    public DocumentVaultServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SafeSeal.DocumentVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new SafeSealStorageOptions(_root);
    }

    [Fact]
    public async Task ImportAsync_StoresEncryptedItemInHiddenVaultAndListsByDisplayName()
    {
        string sourceImage = CreateSourceImage("source.png");
        var service = new DocumentVaultService(_options);

        DocumentEntry imported = await service.ImportAsync(
            sourceImage,
            "Passport Front",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        IReadOnlyList<DocumentEntry> list = await service.ListAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("Passport Front", list[0].DisplayName);
        Assert.True(File.Exists(Path.Combine(_options.VaultDirectory, imported.StoredFileName)));
        Assert.DoesNotContain("source.png", imported.StoredFileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportAsync_AllowsUnicodeDisplayName()
    {
        string sourceImage = CreateSourceImage("source-zh.png");
        var service = new DocumentVaultService(_options);

        DocumentEntry imported = await service.ImportAsync(
            sourceImage,
            "张三",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        IReadOnlyList<DocumentEntry> list = await service.ListAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal("张三", imported.DisplayName);
        Assert.Equal("张三", list[0].DisplayName);
    }

    [Fact]
    public async Task ImportAsync_WithDuplicateNameAndAskOverwrite_ReplacesExistingItem()
    {
        string sourceA = CreateSourceImage("source-a.png");
        string sourceB = CreateSourceImage("source-b.png");

        var service = new DocumentVaultService(_options);

        DocumentEntry first = await service.ImportAsync(
            sourceA,
            "Identity",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        await service.ImportAsync(
            sourceB,
            "Identity",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        IReadOnlyList<DocumentEntry> list = await service.ListAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(first.Id, list[0].Id);
        Assert.Equal("Identity", list[0].DisplayName);
    }

    [Fact]
    public async Task ExportAsync_WritesRenderedOutputFile()
    {
        string source = CreateSourceImage("source-export.png");
        string output = Path.Combine(_root, "output.jpg");

        var service = new DocumentVaultService(_options);
        DocumentEntry imported = await service.ImportAsync(
            source,
            "Visa",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        WatermarkOptions options = new("ONLY FOR {Date}", 0.2, 1);

        await service.ExportAsync(imported.Id, options, output, 85, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task ListAsync_TriggersOneTimeLegacyMigration()
    {
        Directory.CreateDirectory(_options.LegacyDirectory);

        string legacyImagePath = CreateSourceImage("legacy-source.png");
        byte[] raw = await File.ReadAllBytesAsync(legacyImagePath);
        string legacySealPath = Path.Combine(_options.LegacyDirectory, "legacy-id.seal");

        VaultManager.Save(raw, legacySealPath);
        Array.Clear(raw, 0, raw.Length);

        var service = new DocumentVaultService(_options);

        IReadOnlyList<DocumentEntry> entries = await service.ListAsync(CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("legacy-id", entries[0].DisplayName);
        Assert.True(File.Exists(_options.MigrationSentinelPath));
    }

    private string CreateSourceImage(string fileName)
    {
        byte[] pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+X8kAAAAASUVORK5CYII=");
        string path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, pngBytes);
        Array.Clear(pngBytes, 0, pngBytes.Length);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}