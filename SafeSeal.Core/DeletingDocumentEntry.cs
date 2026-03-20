namespace SafeSeal.Core;

public sealed record DeletingDocumentEntry(
    Guid Id,
    string StoredFileName,
    string DeleteOperationId,
    bool RequiresIntervention = false,
    string? InterventionReason = null,
    DateTime? DeletingSinceUtc = null);
