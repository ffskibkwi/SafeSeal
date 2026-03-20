namespace SafeSeal.Core;

public interface ILoggingService
{
    void Trace(
        string source,
        string eventId,
        string? operationId = null,
        IReadOnlyDictionary<string, object?>? fields = null,
        Exception? exception = null);

    void Info(
        string source,
        string eventId,
        string? operationId = null,
        IReadOnlyDictionary<string, object?>? fields = null,
        Exception? exception = null);

    void Warning(
        string source,
        string eventId,
        string? operationId = null,
        IReadOnlyDictionary<string, object?>? fields = null,
        Exception? exception = null);

    void Error(
        string source,
        string eventId,
        string? operationId = null,
        IReadOnlyDictionary<string, object?>? fields = null,
        Exception? exception = null);

    void Critical(
        string source,
        string eventId,
        string? operationId = null,
        IReadOnlyDictionary<string, object?>? fields = null,
        Exception? exception = null);

    void Flush();
}
