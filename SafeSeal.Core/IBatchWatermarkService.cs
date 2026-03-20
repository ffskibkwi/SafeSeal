namespace SafeSeal.Core;

public interface IBatchWatermarkService
{
    Task<BatchResult> ExportAsync(
        IReadOnlyList<string> inputFiles,
        WatermarkOptions watermarkOptions,
        string outputDirectory,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken);
}
