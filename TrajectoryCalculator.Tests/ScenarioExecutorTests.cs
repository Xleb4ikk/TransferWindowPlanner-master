using System.Text.Json;

namespace TrajectoryCalculator.Tests;

public class ScenarioExecutorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "SenarioExecTests_" + Guid.NewGuid().ToString("N"));
    private readonly ScenarioInput _input;
    private readonly JsonSerializerOptions _jsonOptions = ScenarioJson.Options;
    private readonly string _tempInputPath;

    public ScenarioExecutorTests()
    {
        Directory.CreateDirectory(_tempDir);
        _input = SampleScenarioFactory.CreateSingleTemplate();
        _tempInputPath = Path.Combine(_tempDir, "input.json");
        File.WriteAllText(_tempInputPath, JsonSerializer.Serialize(_input, _jsonOptions));
    }

    [Fact]
    public void WithoutInputPath_WritesNothing()
    {
        var result = ScenarioExecutor.Execute(_input, _jsonOptions);
        Assert.Empty(result.WrittenFiles);
    }

    [Fact]
    public void WithInputPath_WritesFile()
    {
        var result = ScenarioExecutor.Execute(_input, _tempInputPath, _jsonOptions);
        Assert.NotEmpty(result.WrittenFiles);
        Assert.True(File.Exists(result.WrittenFiles[0]));
    }

    [Fact]
    public void NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ScenarioExecutor.Execute(null!, _jsonOptions));
        Assert.Throws<ArgumentNullException>(() => ScenarioExecutor.Execute(null!, _tempInputPath, _jsonOptions));
    }

    [Fact]
    public void NullJsonOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ScenarioExecutor.Execute(_input, null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
