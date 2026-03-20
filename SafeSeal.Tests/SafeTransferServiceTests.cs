using System.IO;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class SafeTransferServiceTests : IDisposable
{
    private readonly string _root;
    private readonly SafeSealStorageOptions _options;

    public SafeTransferServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SafeSeal.SafeTransferTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new SafeSealStorageOptions(_root);
    }

    [Fact]
    public async Task CreateAndExtractArchive_RoundTrip_Works()
    {
        string source = CreateImage("safe-transfer-src.png");
        var vault = new DocumentVaultService(_options);
        DocumentEntry entry = await vault.ImportAsync(source, "TransferDoc", NameConflictBehavior.AskOverwrite, CancellationToken.None);

        string archivePath = Path.Combine(_root, "transfer.sstransfer");
        var transfer = new SafeTransferService(_options);

        await transfer.CreateArchiveAsync(entry, archivePath, "123456", CancellationToken.None);
        Assert.True(File.Exists(archivePath));
        Assert.True(transfer.CanReadFormat(archivePath));

        TransferArchiveContent content = await transfer.ExtractArchiveAsync(archivePath, "123456", CancellationToken.None);

        Assert.Equal("image/png", content.MimeType);
        Assert.Contains("TransferDoc", content.OriginalFileName, StringComparison.Ordinal);
        Assert.True(content.ImageData.Length > 0);
    }

    [Fact]
    public async Task ExtractArchiveAsync_WithWrongPin_ThrowsUnauthorizedAccessException()
    {
        string source = CreateImage("safe-transfer-pin.png");
        var vault = new DocumentVaultService(_options);
        DocumentEntry entry = await vault.ImportAsync(source, "PinDoc", NameConflictBehavior.AskOverwrite, CancellationToken.None);

        string archivePath = Path.Combine(_root, "transfer-pin.sstransfer");
        var transfer = new SafeTransferService(_options);

        await transfer.CreateArchiveAsync(entry, archivePath, "123456", CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transfer.ExtractArchiveAsync(archivePath, "654321", CancellationToken.None));
    }

    [Fact]
    public async Task ExtractArchiveAsync_WithTamperedPayload_Throws()
    {
        string source = CreateImage("safe-transfer-tamper.png");
        var vault = new DocumentVaultService(_options);
        DocumentEntry entry = await vault.ImportAsync(source, "TamperDoc", NameConflictBehavior.AskOverwrite, CancellationToken.None);

        string archivePath = Path.Combine(_root, "transfer-tamper.sstransfer");
        var transfer = new SafeTransferService(_options);

        await transfer.CreateArchiveAsync(entry, archivePath, "123456", CancellationToken.None);

        byte[] bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[^1] ^= 0xAA;
        await File.WriteAllBytesAsync(archivePath, bytes);
        Array.Clear(bytes, 0, bytes.Length);

        await Assert.ThrowsAnyAsync<Exception>(() => transfer.ExtractArchiveAsync(archivePath, "123456", CancellationToken.None));
    }

    private string CreateImage(string fileName)
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
            Directory.Delete(_root, true);
        }
    }
}
