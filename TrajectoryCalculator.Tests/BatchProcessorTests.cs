namespace TrajectoryCalculator.Tests;

public class BatchProcessorTests
{
    [Fact]
    public void RunFolder_EmptyDirectory_ReturnsEmptyResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BatchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var processor = new BatchProcessor();
            var result = processor.RunFolder(tempDir, recursive: false, maxDegreeOfParallelism: 1);

            Assert.NotNull(result);
            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RunManifest_NonexistentFile_Throws()
    {
        var processor = new BatchProcessor();
        Assert.Throws<FileNotFoundException>(() => processor.RunManifest("nonexistent.json", maxDegreeOfParallelism: 1));
    }

    [Fact]
    public void RunFolder_WithValidJson_ProcessesAll()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BatchTestsValid_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputA = SampleScenarioFactory.CreateSingleTemplate();
            inputA.Request!.ResultOutputPath = "results-a.json";
            var inputB = SampleScenarioFactory.CreatePorkchopTemplate();
            inputB.Request!.ResultOutputPath = "results-b.json";

            var jsonA = System.Text.Json.JsonSerializer.Serialize(inputA, ScenarioJson.Options);
            var jsonB = System.Text.Json.JsonSerializer.Serialize(inputB, ScenarioJson.Options);
            File.WriteAllText(Path.Combine(tempDir, "a.json"), jsonA);
            File.WriteAllText(Path.Combine(tempDir, "b.json"), jsonB);

            var processor = new BatchProcessor();
            var result = processor.RunFolder(tempDir, recursive: false, maxDegreeOfParallelism: 2);

            Assert.NotNull(result);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
