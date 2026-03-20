namespace SafeSeal.Core;

public sealed record LogEntry
{
    public DateTime UtcTimestamp { get; init; }

    public string Level { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string EventId { get; init; } = string.Empty;

    public string? OperationId { get; init; }

    public IReadOnlyDictionary<string, object?> Fields { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public string? Exception { get; init; }
}
