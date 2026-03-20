namespace SafeSeal.Core;

public interface ITransferPackageService
{
    Task<string> CreateMergedPackageAsync(
        IReadOnlyList<string> inputFiles,
        string pin,
        string outputPath,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);

    Task<BatchResult> ExtractMergedPackageAsync(
        string packagePath,
        string pin,
        string outputDirectory,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);
}
