using System.IO;
using System.Windows.Media;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class BatchWatermarkServiceTests : IDisposable
{
    private readonly string _root;

    public BatchWatermarkServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SafeSeal.BatchWatermarkTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ExportAsync_ContinuesWhenSingleFileFails()
    {
        string good = CreateImage("good.png");
        string bad = Path.Combine(_root, "missing.png");
        string outDir = Path.Combine(_root, "out");

        var service = new BatchWatermarkService();
        WatermarkOptions options = new(["BATCH"], 0.2, 20, 200, 200, 30, Colors.Blue);

        BatchResult result = await service.ExportAsync([good, bad], options, outDir, progress: null, CancellationToken.None);

        Assert.Single(result.OutputFiles);
        Assert.Single(result.Errors);
        Assert.True(File.Exists(result.OutputFiles[0]));
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
