using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SafeSeal.Core;

public sealed class FileLoggingService : ILoggingService, IDisposable
{
    private static readonly string[] SensitiveFieldTokens = ["pin", "key", "secret"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly ManualResetEventSlim _drained = new(true);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _writerThread;

    private int _pendingCount;
    private volatile bool _disposed;

    public FileLoggingService(string? logDirectory = null, int retentionDays = 14)
    {
        if (retentionDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention must be at least one day.");
        }

        _retentionDays = retentionDays;
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SafeSeal", "Logs")
            : logDirectory;

        Directory.CreateDirectory(_logDirectory);
        CleanupExpiredLogs();

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "SafeSeal-FileLogger",
        };

        _writerThread.Start();
    }

    public void Trace(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
        Write("Trace", source, eventId, operationId, fields, exception);
    }

    public void Info(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
        Write("Info", source, eventId, operationId, fields, exception);
    }

    public void Warning(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
        Write("Warning", source, eventId, operationId, fields, exception);
    }

    public void Error(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
        Write("Error", source, eventId, operationId, fields, exception);
    }

    public void Critical(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
        Write("Critical", source, eventId, operationId, fields, exception);
    }

    public void Flush()
    {
        if (_disposed)
        {
            return;
        }

        _signal.Set();
        _drained.Wait(TimeSpan.FromSeconds(10));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Flush();

        _cts.Cancel();
        _signal.Set();
        _writerThread.Join(millisecondsTimeout: 2000);

        _cts.Dispose();
        _signal.Dispose();
        _drained.Dispose();
    }

    private void Write(
        string level,
        string source,
        string eventId,
        string? operationId,
        IReadOnlyDictionary<string, object?>? fields,
        Exception? exception)
    {
        if (_disposed)
        {
            return;
        }

        Dictionary<string, object?> sanitizedFields = SanitizeFields(fields);

        string normalizedSource = string.IsNullOrWhiteSpace(source)
            ? "UnknownSource"
            : source.Trim();

        string normalizedEventId = string.IsNullOrWhiteSpace(eventId)
            ? "unknown_event"
            : eventId.Trim();

        string? exceptionText = exception is null
            ? null
            : level is "Error" or "Critical"
                ? exception.ToString()
                : $"{exception.GetType().Name}: {exception.Message}";

        LogEntry entry = new()
        {
            UtcTimestamp = DateTime.UtcNow,
            Level = level,
            Source = normalizedSource,
            EventId = normalizedEventId,
            OperationId = operationId,
            Fields = sanitizedFields,
            Exception = exceptionText,
        };

        string line = JsonSerializer.Serialize(entry, JsonOptions);

        _drained.Reset();
        Interlocked.Increment(ref _pendingCount);
        _pending.Enqueue(line);
        _signal.Set();
    }

    private void WriterLoop()
    {
        while (true)
        {
            _signal.WaitOne(millisecondsTimeout: 250);
            DrainPendingLines();

            if (_cts.IsCancellationRequested && _pending.IsEmpty)
            {
                break;
            }
        }

        DrainPendingLines();
    }

    private void DrainPendingLines()
    {
        if (_pending.IsEmpty)
        {
            if (Volatile.Read(ref _pendingCount) == 0)
            {
                _drained.Set();
            }

            return;
        }

        List<string> batch = new();
        while (_pending.TryDequeue(out string? line))
        {
            batch.Add(line);
        }

        if (batch.Count == 0)
        {
            if (Volatile.Read(ref _pendingCount) == 0)
            {
                _drained.Set();
            }

            return;
        }

        string logPath = Path.Combine(_logDirectory, $"SafeSeal-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        Directory.CreateDirectory(_logDirectory);

        try
        {
            using FileStream stream = new(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            foreach (string line in batch)
            {
                writer.WriteLine(line);
                MarkBatchWriteComplete();
            }

            writer.Flush();
        }
        catch
        {
            foreach (string _ in batch)
            {
                MarkBatchWriteComplete();
            }
        }
    }

    private void MarkBatchWriteComplete()
    {
        int remaining = Interlocked.Decrement(ref _pendingCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _pendingCount, 0);
            _drained.Set();
        }
    }

    private void CleanupExpiredLogs()
    {
        DateTime cutoffDate = DateTime.UtcNow.Date.AddDays(-_retentionDays);

        foreach (string file in Directory.EnumerateFiles(_logDirectory, "SafeSeal-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith("SafeSeal-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string dateToken = name["SafeSeal-".Length..];
            if (!DateTime.TryParseExact(dateToken, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime logDate))
            {
                continue;
            }

            if (logDate < cutoffDate)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    private static Dictionary<string, object?> SanitizeFields(IReadOnlyDictionary<string, object?>? fields)
    {
        Dictionary<string, object?> result = new(StringComparer.OrdinalIgnoreCase);

        if (fields is null || fields.Count == 0)
        {
            return result;
        }

        foreach ((string key, object? value) in fields)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            string normalizedKey = key.Trim();
            if (IsSensitiveFieldName(normalizedKey))
            {
                result[normalizedKey] = "***REDACTED***";
                continue;
            }

            result[normalizedKey] = NormalizeFieldValue(value);
        }

        return result;
    }

    private static bool IsSensitiveFieldName(string key)
    {
        foreach (string token in SensitiveFieldTokens)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static object? NormalizeFieldValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool b => b,
            sbyte n => n,
            byte n => n,
            short n => n,
            ushort n => n,
            int n => n,
            uint n => n,
            long n => n,
            ulong n => n,
            float n => n,
            double n => n,
            decimal n => n,
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}



