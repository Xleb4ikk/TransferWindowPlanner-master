namespace StandaloneTrajectoryCalculator;

public static class SampleScenarioFactory
{
    public static ScenarioInput CreateSingleTemplate()
    {
        return SolarSystemCatalog.CreateSingleTransferTemplate();
    }

    public static ScenarioInput CreatePorkchopTemplate()
    {
        return SolarSystemCatalog.CreatePorkchopTemplate();
    }
}
