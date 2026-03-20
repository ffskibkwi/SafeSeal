using System.IO;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class TransferPackageServiceTests : IDisposable
{
    private readonly string _root;

    public TransferPackageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SafeSeal.TransferPackageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task CreateAndExtractMergedPackage_RoundTrip_Works()
    {
        string fileA = WriteFile("a.txt", "alpha");
        string fileB = WriteFile("b.txt", "beta");

        string packagePath = Path.Combine(_root, "batch.sstransfer");
        string outputDir = Path.Combine(_root, "out");

        var service = new TransferPackageService();

        await service.CreateMergedPackageAsync([fileA, fileB], "123456", packagePath, progress: null, CancellationToken.None);
        BatchResult result = await service.ExtractMergedPackageAsync(packagePath, "123456", outputDir, progress: null, CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.OutputFiles.Count);
        Assert.Equal("alpha", await File.ReadAllTextAsync(Path.Combine(outputDir, "a.txt")));
        Assert.Equal("beta", await File.ReadAllTextAsync(Path.Combine(outputDir, "b.txt")));
    }

    [Fact]
    public async Task ExtractMergedPackage_WithWrongPin_ThrowsUnauthorizedAccessException()
    {
        string fileA = WriteFile("pin-a.txt", "secret");
        string packagePath = Path.Combine(_root, "pin.sstransfer");

        var service = new TransferPackageService();
        await service.CreateMergedPackageAsync([fileA], "123456", packagePath, progress: null, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExtractMergedPackageAsync(packagePath, "654321", Path.Combine(_root, "x"), progress: null, CancellationToken.None));
    }

    private string WriteFile(string fileName, string content)
    {
        string path = Path.Combine(_root, fileName);
        File.WriteAllText(path, content);
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
