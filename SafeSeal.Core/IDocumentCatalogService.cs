namespace SafeSeal.Core;

public interface IDocumentCatalogService
{
    Task InitializeAsync(CancellationToken ct);

    Task<IReadOnlyList<DocumentEntry>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<DeletingDocumentEntry>> GetDeletingAsync(CancellationToken ct);

    Task<DocumentEntry?> FindByNameAsync(string displayName, CancellationToken ct);

    Task UpsertAsync(DocumentEntry entry, CancellationToken ct);

    Task SoftDeleteAsync(Guid id, CancellationToken ct);

    Task MarkDeletingAsync(Guid id, string opId, DateTime startedUtc, CancellationToken ct);

    Task FinalizeDeleteAsync(Guid id, string opId, DateTime updatedUtc, CancellationToken ct);

    Task RecoverFromDeletingAsync(Guid id, string opId, CancellationToken ct);

    Task MarkInterventionRequiredAsync(Guid id, string opId, string reason, DateTime utc, CancellationToken ct);
}
