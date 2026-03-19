namespace SafeSeal.Core;

public interface IDocumentCatalogService
{
    Task InitializeAsync(CancellationToken ct);

    Task<IReadOnlyList<DocumentEntry>> GetAllAsync(CancellationToken ct);

    Task<DocumentEntry?> FindByNameAsync(string displayName, CancellationToken ct);

    Task UpsertAsync(DocumentEntry entry, CancellationToken ct);

    Task SoftDeleteAsync(Guid id, CancellationToken ct);
}
