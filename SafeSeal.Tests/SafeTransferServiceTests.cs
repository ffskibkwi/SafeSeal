using System.Collections.Concurrent;
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
        InMemoryLoggingService logger = new();
        var transfer = new SafeTransferService(_options, logger);

        await transfer.CreateArchiveAsync(entry, archivePath, "123456", CancellationToken.None);
        Assert.True(File.Exists(archivePath));
        Assert.True(transfer.CanReadFormat(archivePath));

        TransferArchiveContent content = await transfer.ExtractArchiveAsync(archivePath, "123456", CancellationToken.None);

        Assert.Equal("image/png", content.MimeType);
        Assert.Contains("TransferDoc", content.OriginalFileName, StringComparison.Ordinal);
        Assert.True(content.ImageData.Length > 0);

        IReadOnlyList<LogEntry> entries = logger.Entries;
        Assert.Contains(entries, static x => x.EventId == "create_archive_start");
        Assert.Contains(entries, static x => x.EventId == "create_archive_kdf_derived");
        Assert.Contains(entries, static x => x.EventId == "create_archive_written");
        Assert.Contains(entries, static x => x.EventId == "create_archive_end");
        Assert.Contains(entries, static x => x.EventId == "extract_archive_decrypt_success");

        LogEntry? createEnd = entries.LastOrDefault(static x => x.EventId == "create_archive_end");
        Assert.NotNull(createEnd);
        Assert.True(createEnd!.Fields.TryGetValue("success", out object? successValue));
        Assert.True(successValue is bool b && b);
    }

    [Fact]
    public async Task ExtractArchiveAsync_WithWrongPin_LogsAuthFailureAndThrowsUnauthorizedAccessException()
    {
        string source = CreateImage("safe-transfer-pin.png");
        var vault = new DocumentVaultService(_options);
        DocumentEntry entry = await vault.ImportAsync(source, "PinDoc", NameConflictBehavior.AskOverwrite, CancellationToken.None);

        string archivePath = Path.Combine(_root, "transfer-pin.sstransfer");
        InMemoryLoggingService logger = new();
        var transfer = new SafeTransferService(_options, logger);

        await transfer.CreateArchiveAsync(entry, archivePath, "123456", CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transfer.ExtractArchiveAsync(archivePath, "654321", CancellationToken.None));

        IReadOnlyList<LogEntry> entries = logger.Entries;
        LogEntry? authFailed = entries.LastOrDefault(static x => x.EventId == "extract_archive_auth_failed");
        Assert.NotNull(authFailed);

        LogEntry? extractFailed = entries.LastOrDefault(x => x.EventId == "extract_archive_failed" && x.OperationId == authFailed!.OperationId);
        Assert.NotNull(extractFailed);
    }

    [Fact]
    public async Task ExtractArchiveAsync_WithTamperedPayload_ThrowsAndLogsFailureOperation()
    {
        string source = CreateImage("safe-transfer-tamper.png");
        var vault = new DocumentVaultService(_options);
        DocumentEntry entry = await vault.ImportAsync(source, "TamperDoc", NameConflictBehavior.AskOverwrite, CancellationToken.None);

        string archivePath = Path.Combine(_root, "transfer-tamper.sstransfer");
        InMemoryLoggingService logger = new();
        var transfer = new SafeTransferService(_options, logger);

        await transfer.CreateArchiveAsync(entry, archivePath, "123456", CancellationToken.None);

        byte[] bytes = await File.ReadAllBytesAsync(archivePath);
        bytes[^1] ^= 0xAA;
        await File.WriteAllBytesAsync(archivePath, bytes);
        Array.Clear(bytes, 0, bytes.Length);

        await Assert.ThrowsAnyAsync<Exception>(() => transfer.ExtractArchiveAsync(archivePath, "123456", CancellationToken.None));

        LogEntry? failure = logger.Entries.LastOrDefault(static x => x.EventId == "extract_archive_failed");
        Assert.NotNull(failure);
        Assert.False(string.IsNullOrWhiteSpace(failure!.OperationId));
        Assert.True(failure.Fields.ContainsKey("phase"));
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

    private sealed class InMemoryLoggingService : ILoggingService
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

        public void Trace(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
        {
            Add("Trace", source, eventId, operationId, fields, exception);
        }

        public void Info(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
        {
            Add("Info", source, eventId, operationId, fields, exception);
        }

        public void Warning(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
        {
            Add("Warning", source, eventId, operationId, fields, exception);
        }

        public void Error(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
        {
            Add("Error", source, eventId, operationId, fields, exception);
        }

        public void Critical(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
        {
            Add("Critical", source, eventId, operationId, fields, exception);
        }

        public void Flush()
        {
        }

        private void Add(string level, string source, string eventId, string? operationId, IReadOnlyDictionary<string, object?>? fields, Exception? exception)
        {
            Dictionary<string, object?> snapshot = fields is null
                ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object?>(fields, StringComparer.OrdinalIgnoreCase);

            _entries.Enqueue(new LogEntry
            {
                UtcTimestamp = DateTime.UtcNow,
                Level = level,
                Source = source,
                EventId = eventId,
                OperationId = operationId,
                Fields = snapshot,
                Exception = exception?.ToString(),
            });
        }
    }
}

