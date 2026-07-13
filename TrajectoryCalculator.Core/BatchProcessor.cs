namespace TrajectoryCalculator;

/// <summary>
/// High-level batch processor using <see cref="BatchCalculator"/>.
/// </summary>
public sealed class BatchProcessor : IBatchCalculator
{
    private readonly TrajectoryCalculatorOptions _options;

    public BatchProcessor()
        : this(new TrajectoryCalculatorOptions())
    {
    }

    public BatchProcessor(TrajectoryCalculatorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public BatchRunResult RunFolder(string folderPath, bool recursive, int maxDegreeOfParallelism)
    {
        return BatchCalculator.RunFolder(folderPath, recursive, new BatchCalculator.Options
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        });
    }

    public BatchRunResult RunManifest(string manifestPath, int maxDegreeOfParallelism)
    {
        return BatchCalculator.RunManifestFile(manifestPath, new BatchCalculator.Options
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        });
    }
}
