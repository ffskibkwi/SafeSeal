namespace SafeSeal.Core;

public sealed record BatchProgress(
    int Total,
    int Completed,
    int Failed,
    string? CurrentFile,
    string Stage);
