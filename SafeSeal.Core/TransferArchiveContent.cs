namespace SafeSeal.Core;

public sealed record TransferArchiveContent(
    string OriginalFileName,
    long OriginalFileSize,
    string MimeType,
    DateTime CreatedAt,
    WatermarkOptions? WatermarkOptions,
    byte[] ImageData);
