namespace SafeSeal.Core;

public sealed record DocumentEntry(
    Guid Id,
    string DisplayName,
    string StoredFileName,
    string OriginalExtension,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
