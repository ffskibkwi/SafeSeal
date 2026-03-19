namespace SafeSeal.Core;

public sealed record ExportMetadataContext(
    string SignatureId,
    string TemplateId,
    int TemplateVersion,
    DateTime ExportUtc);
