using System.IO;
using SafeSeal.Core;
using Xunit;

namespace SafeSeal.Tests;

public sealed class DeleteInterventionTests : IDisposable
{
    private readonly string _root;
    private readonly SafeSealStorageOptions _options;

    public DeleteInterventionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SafeSeal.DeleteInterventionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _options = new SafeSealStorageOptions(_root);
    }

    [Fact]
    public async Task DeleteAsync_WhenCompensationFails_MarksManualInterventionRequired()
    {
        Guid id = Guid.NewGuid();
        DocumentEntry entry = new(
            id,
            "InterventionDoc",
            "invalid-stored-name",
            ".png",
            DateTime.UtcNow,
            DateTime.UtcNow);

        var fakeCatalog = new FakeCatalog(entry);
        var storage = new HiddenVaultStorageService(_options);
        var service = new DocumentVaultService(_options, fakeCatalog, storage, new WatermarkRenderer(), new ExportService(), new TraceDeleteOperationLog());

        await Assert.ThrowsAnyAsync<Exception>(() => service.DeleteAsync(id, CancellationToken.None));

        Assert.True(fakeCatalog.MarkInterventionRequiredCalled);
        Assert.True(fakeCatalog.RecoverCalled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class FakeCatalog : IDocumentCatalogService
    {
        private readonly DocumentEntry _entry;

        public FakeCatalog(DocumentEntry entry)
        {
            _entry = entry;
        }

        public bool RecoverCalled { get; private set; }

        public bool MarkInterventionRequiredCalled { get; private set; }

        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<DocumentEntry>> GetAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DocumentEntry>>([_entry]);

        public Task<IReadOnlyList<DeletingDocumentEntry>> GetDeletingAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DeletingDocumentEntry>>([]);

        public Task<DocumentEntry?> FindByNameAsync(string displayName, CancellationToken ct) => Task.FromResult<DocumentEntry?>(null);

        public Task UpsertAsync(DocumentEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task SoftDeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        public Task MarkDeletingAsync(Guid id, string opId, DateTime startedUtc, CancellationToken ct) => Task.CompletedTask;

        public Task FinalizeDeleteAsync(Guid id, string opId, DateTime updatedUtc, CancellationToken ct) => Task.CompletedTask;

        public Task RecoverFromDeletingAsync(Guid id, string opId, CancellationToken ct)
        {
            RecoverCalled = true;
            throw new IOException("Simulated recover failure.");
        }

        public Task MarkInterventionRequiredAsync(Guid id, string opId, string reason, DateTime utc, CancellationToken ct)
        {
            MarkInterventionRequiredCalled = true;
            return Task.CompletedTask;
        }
    }
}
