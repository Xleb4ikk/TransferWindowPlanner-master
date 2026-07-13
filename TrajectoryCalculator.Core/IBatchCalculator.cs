namespace TrajectoryCalculator;

/// <summary>
/// Interface for batch trajectory calculation.
/// </summary>
public interface IBatchCalculator
{
    /// <summary>
    /// Runs batch calculation on all JSON files in a folder.
    /// </summary>
    BatchRunResult RunFolder(string folderPath, bool recursive, int maxDegreeOfParallelism);

    /// <summary>
    /// Runs batch calculation from a manifest file listing JSON paths.
    /// </summary>
    BatchRunResult RunManifest(string manifestPath, int maxDegreeOfParallelism);
}
