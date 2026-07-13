namespace TrajectoryCalculator;

/// <summary>
/// High-level trajectory planner.
/// </summary>
public sealed class TrajectoryPlanner : ITrajectoryCalculator
{
    private readonly TrajectoryCalculatorOptions _options;

    /// <summary>
    /// Creates a planner with default options.
    /// </summary>
    public TrajectoryPlanner()
        : this(new TrajectoryCalculatorOptions())
    {
    }

    /// <summary>
    /// Creates a planner with custom options.
    /// </summary>
    public TrajectoryPlanner(TrajectoryCalculatorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Calculates a trajectory from the specified mission scenario, without writing files to disk.
    /// </summary>
    public ExecutionResult Calculate(ScenarioInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ScenarioExecutor.Execute(input, ScenarioJson.Options);
    }

    /// <summary>
    /// Loads a scenario from a JSON file and calculates the trajectory.
    /// </summary>
    public ExecutionResult CalculateFromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var input = ScenarioJson.LoadFromFile(path)
            ?? throw new InvalidOperationException($"Failed to parse scenario from '{path}'.");
        return ScenarioExecutor.Execute(input, path, ScenarioJson.Options);
    }
}
