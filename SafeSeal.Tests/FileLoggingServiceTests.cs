using System.Text.Json;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class FileLoggingServiceTests : IDisposable
{
    private readonly string _logDirectory;

    public FileLoggingServiceTests()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), "SafeSeal.FileLoggingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logDirectory);
    }

    [Fact]
    public void ConcurrentWrites_ProduceValidJsonLinesWithoutCorruption()
    {
        const int total = 240;

        using FileLoggingService logger = new(_logDirectory, retentionDays: 14);

        Parallel.For(0, total, i =>
        {
            logger.Info(
                "FileLoggingServiceTests",
                "concurrent_write",
                operationId: $"op-{i}",
                fields: new Dictionary<string, object?>
                {
                    ["index"] = i,
                    ["pin"] = "123456",
                    ["key"] = "abc",
                });
        });

        logger.Flush();

        string logFile = Directory.GetFiles(_logDirectory, "SafeSeal-*.jsonl", SearchOption.TopDirectoryOnly).Single();
        string[] lines = File.ReadAllLines(logFile);

        Assert.Equal(total, lines.Length);

        foreach (string line in lines)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("utcTimestamp", out _));
            Assert.True(root.TryGetProperty("level", out JsonElement level));
            Assert.Equal("Info", level.GetString());
            Assert.True(root.TryGetProperty("eventId", out JsonElement eventId));
            Assert.Equal("concurrent_write", eventId.GetString());

            JsonElement fields = root.GetProperty("fields");
            Assert.Equal("***REDACTED***", fields.GetProperty("pin").GetString());
            Assert.Equal("***REDACTED***", fields.GetProperty("key").GetString());
        }
    }

    [Fact]
    public void ErrorLogging_IncludesFullExceptionStackTrace()
    {
        using FileLoggingService logger = new(_logDirectory, retentionDays: 14);

        Exception captured;
        try
        {
            ThrowSampleFailure();
            throw new InvalidOperationException("Expected test failure was not thrown.");
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        logger.Error("FileLoggingServiceTests", "stack_trace_capture", exception: captured);
        logger.Flush();

        string logFile = Directory.GetFiles(_logDirectory, "SafeSeal-*.jsonl", SearchOption.TopDirectoryOnly).Single();
        string line = File.ReadAllLines(logFile).Last();

        using JsonDocument doc = JsonDocument.Parse(line);
        string exceptionText = doc.RootElement.GetProperty("exception").GetString() ?? string.Empty;

        Assert.Contains(nameof(ThrowSampleFailure), exceptionText, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", exceptionText, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRetentionCleanup_DeletesOnlyExpiredFiles()
    {
        string oldToken = DateTime.UtcNow.AddDays(-45).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        string recentToken = DateTime.UtcNow.AddDays(-2).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        string oldFile = Path.Combine(_logDirectory, $"SafeSeal-{oldToken}.jsonl");
        string recentFile = Path.Combine(_logDirectory, $"SafeSeal-{recentToken}.jsonl");

        File.WriteAllText(oldFile, "old");
        File.WriteAllText(recentFile, "recent");

        using FileLoggingService logger = new(_logDirectory, retentionDays: 14);
        logger.Flush();

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(recentFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private static void ThrowSampleFailure()
    {
        throw new InvalidOperationException("boom");
    }
}
