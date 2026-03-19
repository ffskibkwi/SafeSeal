using System.Windows.Media.Imaging;

namespace SafeSeal.Core;

public interface IDocumentVaultService
{
    Task<IReadOnlyList<DocumentEntry>> ListAsync(CancellationToken ct);

    Task<DocumentEntry> ImportAsync(
        string sourceImagePath,
        string displayName,
        NameConflictBehavior behavior,
        CancellationToken ct);

    Task<BitmapSource> BuildPreviewAsync(Guid documentId, WatermarkOptions options, CancellationToken ct);

    Task ExportAsync(Guid documentId, WatermarkOptions options, string outputPath, int jpegQuality, CancellationToken ct);

    Task RenameAsync(Guid documentId, string newDisplayName, CancellationToken ct);

    Task DeleteAsync(Guid documentId, CancellationToken ct);
}
