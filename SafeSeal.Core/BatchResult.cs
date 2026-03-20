namespace SafeSeal.Core;

public sealed record BatchResult(
    IReadOnlyList<string> OutputFiles,
    IReadOnlyList<BatchFileError> Errors,
    TimeSpan Elapsed);
