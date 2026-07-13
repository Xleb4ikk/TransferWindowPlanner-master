namespace TrajectoryCalculator;

/// <summary>
/// Interface for trajectory calculation.
/// </summary>
public interface ITrajectoryCalculator
{
    /// <summary>
    /// Calculates a trajectory from the specified mission scenario, without writing files to disk.
    /// </summary>
    ExecutionResult Calculate(ScenarioInput input);
}
