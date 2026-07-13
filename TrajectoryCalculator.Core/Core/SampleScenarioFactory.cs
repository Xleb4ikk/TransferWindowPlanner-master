namespace TrajectoryCalculator;

/// <summary>
/// Pre-built scenario templates for common missions (Earth→Mars).
/// </summary>
public static class SampleScenarioFactory
{
    /// <summary>
    /// Creates a sample Earth-to-Mars single-transfer scenario.
    /// The result is available via ExecutionResult.Transfer.
    /// </summary>
    public static ScenarioInput CreateSingleTemplate()
    {
        return SolarSystemCatalog.CreateSingleTransferTemplate();
    }

    /// <summary>
    /// Creates a sample Earth-to-Mars porkchop scenario.
    /// The result is available via ExecutionResult.Porkchop.
    /// </summary>
    public static ScenarioInput CreatePorkchopTemplate()
    {
        return SolarSystemCatalog.CreatePorkchopTemplate();
    }
}
