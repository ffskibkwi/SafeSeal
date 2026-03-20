using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace SafeSeal.Core;

public sealed class BatchWatermarkService : IBatchWatermarkService
{
    private readonly WatermarkRenderer _renderer;
    private readonly ExportService _exportService;

    public BatchWatermarkService()
        : this(new WatermarkRenderer(), new ExportService())
    {
    }

    public BatchWatermarkService(WatermarkRenderer renderer, ExportService exportService)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    public async Task<BatchResult> ExportAsync(
        IReadOnlyList<string> inputFiles,
        WatermarkOptions watermarkOptions,
        string outputDirectory,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputFiles);
        ArgumentNullException.ThrowIfNull(watermarkOptions);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory cannot be empty.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);

        Stopwatch stopwatch = Stopwatch.StartNew();
        ConcurrentBag<string> outputFiles = new();
        ConcurrentBag<BatchFileError> errors = new();

        int total = inputFiles.Count;
        int completed = 0;
        int failed = 0;

        progress?.Report(new BatchProgress(total, 0, 0, null, "Starting"));

        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(inputFiles, options, async (inputPath, ct) =>
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                Interlocked.Increment(ref failed);
                errors.Add(new BatchFileError(inputPath ?? string.Empty, "FILE_NOT_FOUND", "Input file was not found."));
                progress?.Report(new BatchProgress(total, Volatile.Read(ref completed), Volatile.Read(ref failed), inputPath, "Failed"));
                return;
            }

            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(inputPath, ct).ConfigureAwait(false);
                using SecureBufferScope secure = new(bytes);

                var rendered = _renderer.Render(secure.Buffer, watermarkOptions);
                string outputPath = BuildOutputPath(outputDirectory, inputPath);

                bool ok = string.Equals(Path.GetExtension(outputPath), ".png", StringComparison.OrdinalIgnoreCase)
                    ? _exportService.ExportAsPng(rendered, outputPath)
                    : _exportService.ExportAsJpeg(rendered, outputPath, 85);

                if (!ok)
                {
                    Trace.TraceWarning("Batch export completed without metadata: {0}", outputPath);
                }

                outputFiles.Add(outputPath);
                Interlocked.Increment(ref completed);
                progress?.Report(new BatchProgress(total, Volatile.Read(ref completed), Volatile.Read(ref failed), Path.GetFileName(inputPath), "Completed"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                errors.Add(new BatchFileError(inputPath, "PROCESS_ERROR", ex.Message));
                progress?.Report(new BatchProgress(total, Volatile.Read(ref completed), Volatile.Read(ref failed), Path.GetFileName(inputPath), "Failed"));
            }
        }).ConfigureAwait(false);

        stopwatch.Stop();
        return new BatchResult(outputFiles.ToArray(), errors.ToArray(), stopwatch.Elapsed);
    }

    private static string BuildOutputPath(string outputDirectory, string inputPath)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
        string extension = Path.GetExtension(inputPath);

        string outputExtension = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ".jpg",
            _ => ".png",
        };

        string outputFileName = $"{fileNameWithoutExtension}_safe{outputExtension}";
        return Path.Combine(outputDirectory, outputFileName);
    }
}
