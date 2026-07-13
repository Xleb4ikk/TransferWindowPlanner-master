namespace TrajectoryCalculator.Tests;

public class TrajectoryPlannerTests
{
    [Fact]
    public void Calculate_ReturnsResult()
    {
        var planner = new TrajectoryPlanner();
        var input = SampleScenarioFactory.CreateSingleTemplate();

        var result = planner.Calculate(input);

        Assert.NotNull(result);
        Assert.NotNull(result.Transfer);
        Assert.InRange(result.Transfer.DVTotal, 7_000, 10_000);
    }

    [Fact]
    public void Calculate_Deterministic()
    {
        var planner = new TrajectoryPlanner();
        var input = SampleScenarioFactory.CreateSingleTemplate();

        var r1 = planner.Calculate(input);
        var r2 = planner.Calculate(input);

        Assert.Equal(r1.Transfer!.DVTotal, r2.Transfer!.DVTotal);
    }

    [Fact]
    public void Calculate_DoesNotMutateInput()
    {
        var planner = new TrajectoryPlanner();
        var input = SampleScenarioFactory.CreateSingleTemplate();
        var originalSerialized = System.Text.Json.JsonSerializer.Serialize(input, ScenarioJson.Options);

        planner.Calculate(input);

        var afterSerialized = System.Text.Json.JsonSerializer.Serialize(input, ScenarioJson.Options);
        Assert.Equal(originalSerialized, afterSerialized);
    }

    [Fact]
    public void Calculate_NullInput_Throws()
    {
        var planner = new TrajectoryPlanner();
        Assert.Throws<ArgumentNullException>(() => planner.Calculate(null!));
    }

    [Fact]
    public void CalculateFromFile_NullPath_Throws()
    {
        var planner = new TrajectoryPlanner();
        Assert.Throws<ArgumentNullException>(() => planner.CalculateFromFile(null!));
    }

    [Fact]
    public void CalculateFromFile_NonexistentPath_Throws()
    {
        var planner = new TrajectoryPlanner();
        Assert.Throws<InvalidOperationException>(() => planner.CalculateFromFile("nonexistent.json"));
    }

    [Fact]
    public void CalculateFromFile_ValidFile_ReturnsResult()
    {
        var planner = new TrajectoryPlanner();
        var input = SampleScenarioFactory.CreateSingleTemplate();
        var tempDir = Path.Combine(Path.GetTempPath(), "TPlannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "input.json");
        File.WriteAllText(tempFile, System.Text.Json.JsonSerializer.Serialize(input, ScenarioJson.Options));

        try
        {
            var result = planner.CalculateFromFile(tempFile);
            Assert.NotNull(result);
            Assert.NotEmpty(result.WrittenFiles);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
