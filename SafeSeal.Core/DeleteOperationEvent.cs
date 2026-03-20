namespace SafeSeal.Core;

public sealed record DeleteOperationEvent(
    Guid DocumentId,
    string OperationId,
    string Phase,
    string Result,
    string Message,
    DateTime Utc);
