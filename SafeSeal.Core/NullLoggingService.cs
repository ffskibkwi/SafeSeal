namespace SafeSeal.Core;

public sealed class NullLoggingService : ILoggingService
{
    public void Trace(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
    }

    public void Info(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
    }

    public void Warning(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
    }

    public void Error(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
    }

    public void Critical(string source, string eventId, string? operationId = null, IReadOnlyDictionary<string, object?>? fields = null, Exception? exception = null)
    {
    }

    public void Flush()
    {
    }
}