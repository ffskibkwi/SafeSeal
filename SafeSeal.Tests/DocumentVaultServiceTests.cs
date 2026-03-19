using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
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
    public async Task ImportRenameDeleteReimport_ExactUnicodeName_ZhangSan_Succeeds()
    {
        string sourceA = CreateSourceImage("source-zh-a.png");
        string sourceB = CreateSourceImage("source-zh-b.png");

        var service = new DocumentVaultService(_options);

        DocumentEntry first = await service.ImportAsync(
            sourceA,
            "张三",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        await service.RenameAsync(first.Id, "张四", CancellationToken.None);
        await service.RenameAsync(first.Id, "张三", CancellationToken.None);
        await service.DeleteAsync(first.Id, CancellationToken.None);

        DocumentEntry second = await service.ImportAsync(
            sourceB,
            "张三",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        IReadOnlyList<DocumentEntry> list = await service.ListAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(second.Id, list[0].Id);
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
    public async Task HiddenVaultStorageService_RejectsInvalidStoredFileNames()
    {
        var storage = new HiddenVaultStorageService(_options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.LoadAsync("..\\..\\evil.seal", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync("C:\\Windows\\not-safe.seal", CancellationToken.None));
    }

    [Fact]
    public async Task BuildPreviewAsync_WithTamperedStoredFileName_ThrowsInvalidOperationException()
    {
        string source = CreateSourceImage("tamper-source.png");
        var service = new DocumentVaultService(_options);

        DocumentEntry imported = await service.ImportAsync(
            source,
            "Tamper",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        await TamperStoredFileNameAsync(imported.Id, "..\\..\\Windows\\win.ini");

        WatermarkOptions options = new(["SAFE"], 0.2, 20, 220, 220, 35, Colors.Blue);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildPreviewAsync(imported.Id, options, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WhenPhysicalDeleteFails_RecoversDocumentToActive()
    {
        string source = CreateSourceImage("delete-recover.png");
        var service = new DocumentVaultService(_options);

        DocumentEntry imported = await service.ImportAsync(
            source,
            "Recoverable",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        string storedPath = Path.Combine(_options.VaultDirectory, imported.StoredFileName);
        using FileStream lockHandle = new(storedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => service.DeleteAsync(imported.Id, CancellationToken.None));

        IReadOnlyList<DocumentEntry> list = await service.ListAsync(CancellationToken.None);
        Assert.Contains(list, item => item.Id == imported.Id);

        IDocumentCatalogService catalog = new DocumentCatalogService(_options);
        await catalog.InitializeAsync(CancellationToken.None);
        IReadOnlyList<DeletingDocumentEntry> deleting = await catalog.GetDeletingAsync(CancellationToken.None);
        Assert.Empty(deleting);
    }

    [Fact]
    public async Task StartupReconciliation_DeletingWithFilePresent_RecoversToActive()
    {
        string source = CreateSourceImage("reconcile-present.png");
        var service = new DocumentVaultService(_options);

        DocumentEntry imported = await service.ImportAsync(
            source,
            "Recover On Startup",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        IDocumentCatalogService catalog = new DocumentCatalogService(_options);
        await catalog.InitializeAsync(CancellationToken.None);

        string opId = Guid.NewGuid().ToString("N");
        await catalog.MarkDeletingAsync(imported.Id, opId, DateTime.UtcNow, CancellationToken.None);

        var restarted = new DocumentVaultService(_options);
        IReadOnlyList<DocumentEntry> list = await restarted.ListAsync(CancellationToken.None);

        Assert.Contains(list, entry => entry.Id == imported.Id);
        IReadOnlyList<DeletingDocumentEntry> deleting = await catalog.GetDeletingAsync(CancellationToken.None);
        Assert.Empty(deleting);
    }

    [Fact]
    public async Task StartupReconciliation_DeletingWithMissingFile_FinalizesDeleted()
    {
        string source = CreateSourceImage("reconcile-missing.png");
        var service = new DocumentVaultService(_options);

        DocumentEntry imported = await service.ImportAsync(
            source,
            "Finalize On Startup",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        IDocumentCatalogService catalog = new DocumentCatalogService(_options);
        await catalog.InitializeAsync(CancellationToken.None);

        string opId = Guid.NewGuid().ToString("N");
        await catalog.MarkDeletingAsync(imported.Id, opId, DateTime.UtcNow, CancellationToken.None);

        string storedPath = Path.Combine(_options.VaultDirectory, imported.StoredFileName);
        File.Delete(storedPath);

        var restarted = new DocumentVaultService(_options);
        IReadOnlyList<DocumentEntry> list = await restarted.ListAsync(CancellationToken.None);

        Assert.DoesNotContain(list, entry => entry.Id == imported.Id);
        Assert.Null(await catalog.FindByNameAsync("Finalize On Startup", CancellationToken.None));
    }

    [Fact]
    public async Task ExportAsync_RejectsUnsupportedExtension()
    {
        string source = CreateSourceImage("export-invalid-ext.png");
        string output = Path.Combine(_root, "output.gif");

        var service = new DocumentVaultService(_options);
        DocumentEntry imported = await service.ImportAsync(
            source,
            "Ext Validation",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        WatermarkOptions options = new(["SAFE"], 0.2, 20, 240, 240, 35, Colors.Blue);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(imported.Id, options, output, 85, CancellationToken.None));
        Assert.Contains("Unsupported export format", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_EmbedsMetadataAndWritesRenderedOutputFile()
    {
        string source = CreateSourceImage("source-export.png");
        string output = Path.Combine(_root, "output.png");

        var service = new DocumentVaultService(_options);
        DocumentEntry imported = await service.ImportAsync(
            source,
            "Visa",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        WatermarkOptions options = new(
            ["ONLY FOR TEST"],
            0.25,
            26,
            220,
            200,
            30,
            Color.FromRgb(0x25, 0x63, 0xEB),
            "standard-use",
            1,
            null);

        await service.ExportAsync(imported.Id, options, output, 85, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);

                byte[] exported = await File.ReadAllBytesAsync(output);
        string metadataText = System.Text.Encoding.UTF8.GetString(exported);

        Assert.Contains("SafeSeal.SignatureId", metadataText, StringComparison.Ordinal);
        Assert.Contains("SafeSeal.TemplateId", metadataText, StringComparison.Ordinal);
        Assert.Contains("standard-use", metadataText, StringComparison.Ordinal);
        Assert.Contains("SafeSeal.TemplateVersion", metadataText, StringComparison.Ordinal);
        Array.Clear(exported, 0, exported.Length);
    }

    [Fact]
    public void WatermarkRenderer_SignatureLayer_ChangesPixels()
    {
        byte[] source = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAQAAACEN3D/AAAAE0lEQVR42mNk+M+ABzDgP4MBABPuAq3oMte6AAAAAElFTkSuQmCC");
        var renderer = new WatermarkRenderer();

        WatermarkOptions baseOptions = new(
            ["BLOCK"],
            0.22,
            20,
            180,
            160,
            35,
            Colors.Blue,
            "standard-use",
            1,
            null);

        BitmapSource withoutSig = renderer.Render((byte[])source.Clone(), baseOptions);
        BitmapSource withSig = renderer.Render((byte[])source.Clone(), baseOptions.WithSignature("abcd1234ef567890"));

        byte[] pixelsA = ExtractPixels(withoutSig);
        byte[] pixelsB = ExtractPixels(withSig);

        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(pixelsA)), Convert.ToHexString(SHA256.HashData(pixelsB)));
    }

    [Fact]
    public async Task ConcurrentImportExportDelete_RunsWithoutStateCorruption()
    {
        string source1 = CreateSourceImage("race-1.png");
        string source2 = CreateSourceImage("race-2.png");
        string source3 = CreateSourceImage("race-3.png");

        var service = new DocumentVaultService(_options);

        DocumentEntry doc1 = await service.ImportAsync(source1, "Race-1", NameConflictBehavior.AskOverwrite, CancellationToken.None);
        DocumentEntry doc2 = await service.ImportAsync(source2, "Race-2", NameConflictBehavior.AskOverwrite, CancellationToken.None);
        DocumentEntry doc3 = await service.ImportAsync(source3, "Race-3", NameConflictBehavior.AskOverwrite, CancellationToken.None);

        string exportPath = Path.Combine(_root, "race-output.jpg");
        WatermarkOptions options = new(["RACE"], 0.2, 22, 210, 180, 35, Colors.Blue);

        Task exportTask = service.ExportAsync(doc1.Id, options, exportPath, 85, CancellationToken.None);
        Task renameTask = service.RenameAsync(doc2.Id, "Race-2-Renamed", CancellationToken.None);
        Task deleteTask = service.DeleteAsync(doc3.Id, CancellationToken.None);

        await Task.WhenAll(exportTask, renameTask, deleteTask);

        IReadOnlyList<DocumentEntry> list = await service.ListAsync(CancellationToken.None);
        Assert.True(File.Exists(exportPath));
        Assert.DoesNotContain(list, x => x.Id == doc3.Id);
        Assert.Contains(list, x => x.DisplayName == "Race-2-Renamed");
    }

    [Fact]
    public async Task LargeFile_ImportPreviewExport_StaysStable()
    {
        string largeBmp = CreateLargeBmpSource("large.bmp", 3600, 1100);
        string output = Path.Combine(_root, "large-export.jpg");

        var service = new DocumentVaultService(_options);
        DocumentEntry imported = await service.ImportAsync(
            largeBmp,
            "Large Doc",
            NameConflictBehavior.AskOverwrite,
            CancellationToken.None);

        WatermarkOptions options = new(["LARGE"], 0.24, 24, 260, 220, 40, Colors.Blue);

        BitmapSource preview = await service.BuildPreviewAsync(imported.Id, options, CancellationToken.None);
        await service.ExportAsync(imported.Id, options, output, 85, CancellationToken.None);

        Assert.True(preview.PixelWidth > 0);
        Assert.True(preview.PixelHeight > 0);
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

    private async Task TamperStoredFileNameAsync(Guid id, string storedFileName)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _options.CatalogPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };

        await using SqliteConnection connection = new(builder.ToString());
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Documents SET StoredFileName = $storedFileName WHERE Id = $id;";
        command.Parameters.AddWithValue("$storedFileName", storedFileName);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private string CreateSourceImage(string fileName)
    {
        byte[] pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO7+X8kAAAAASUVORK5CYII=");
        string path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, pngBytes);
        Array.Clear(pngBytes, 0, pngBytes.Length);
        return path;
    }

    private string CreateLargeBmpSource(string fileName, int width, int height)
    {
        int bytesPerPixel = 3;
        int rowStride = ((width * bytesPerPixel) + 3) & ~3;
        int pixelBytes = rowStride * height;
        int fileSize = 54 + pixelBytes;

        byte[] bmp = new byte[fileSize];

        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32LE(bmp, 2, fileSize);
        WriteInt32LE(bmp, 10, 54);
        WriteInt32LE(bmp, 14, 40);
        WriteInt32LE(bmp, 18, width);
        WriteInt32LE(bmp, 22, height);
        WriteInt16LE(bmp, 26, 1);
        WriteInt16LE(bmp, 28, 24);
        WriteInt32LE(bmp, 34, pixelBytes);

        int offset = 54;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = offset + (x * bytesPerPixel);
                byte channel = (byte)((x + y) % 251);
                bmp[pixelIndex] = channel;
                bmp[pixelIndex + 1] = (byte)(255 - channel);
                bmp[pixelIndex + 2] = (byte)((channel / 2) + 64);
            }

            offset += rowStride;
        }

        string path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, bmp);
        Array.Clear(bmp, 0, bmp.Length);
        return path;
    }

    private static void WriteInt16LE(byte[] target, int offset, short value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32LE(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
        target[offset + 2] = (byte)((value >> 16) & 0xFF);
        target[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static byte[] ExtractPixels(BitmapSource image)
    {
        int stride = image.PixelWidth * 4;
        byte[] pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}



