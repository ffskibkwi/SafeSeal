namespace SafeSeal.Core;

public interface ISafeTransferService
{
    Task CreateArchiveAsync(DocumentEntry document, string archivePath, string pin, CancellationToken ct = default);

    Task<TransferArchiveContent> ExtractArchiveAsync(string archivePath, string pin, CancellationToken ct = default);

    bool CanReadFormat(string filePath);
}
