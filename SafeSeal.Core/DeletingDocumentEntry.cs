namespace SafeSeal.Core;

public sealed record DeletingDocumentEntry(
    Guid Id,
    string StoredFileName,
    string DeleteOperationId);
