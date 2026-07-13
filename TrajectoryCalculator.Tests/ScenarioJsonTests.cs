using System.Text.Json;

namespace TrajectoryCalculator.Tests;

public class ScenarioJsonTests
{
    [Fact]
    public void RoundTrip_ProducesEqualFields()
    {
        var original = SampleScenarioFactory.CreateSingleTemplate();
        var json = JsonSerializer.Serialize(original, ScenarioJson.Options);

        var deserialized = JsonSerializer.Deserialize<ScenarioInput>(json, ScenarioJson.Options);
        Assert.NotNull(deserialized);

        Assert.Equal(original.Calendar.Kind, deserialized.Calendar.Kind);
        Assert.Equal(original.Calendar.DaysPerYear, deserialized.Calendar.DaysPerYear);
        Assert.Equal(original.Calendar.HoursPerDay, deserialized.Calendar.HoursPerDay);

        Assert.Equal(original.CentralBody.Name, deserialized.CentralBody.Name);
        Assert.Equal(original.CentralBody.GravitationalParameter, deserialized.CentralBody.GravitationalParameter);

        Assert.Equal(original.Bodies.Count, deserialized.Bodies.Count);
        for (int i = 0; i < original.Bodies.Count; i++)
        {
            Assert.Equal(original.Bodies[i].Name, deserialized.Bodies[i].Name);
            Assert.Equal(original.Bodies[i].GravitationalParameter, deserialized.Bodies[i].GravitationalParameter);
            Assert.Equal(original.Bodies[i].Radius, deserialized.Bodies[i].Radius);
        }

        Assert.NotNull(original.Request);
        Assert.NotNull(deserialized.Request);
        Assert.Equal(original.Request.Mode, deserialized.Request.Mode);
        Assert.Equal(original.Request.Origin, deserialized.Request.Origin);
        Assert.Equal(original.Request.Destination, deserialized.Request.Destination);
        Assert.Equal(original.Request.DepartureTime, deserialized.Request.DepartureTime);
        Assert.Equal(original.Request.TravelTime, deserialized.Request.TravelTime);
    }

    [Fact]
    public void SaveAndLoad_ProducesEqualFields()
    {
        var original = SampleScenarioFactory.CreateSingleTemplate();
        var tempDir = Path.Combine(Path.GetTempPath(), "JsonRoundTrip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "scenario.json");

        try
        {
            ScenarioJson.SaveToFile(original, tempFile);
            Assert.True(File.Exists(tempFile));

            var loaded = ScenarioJson.LoadFromFile(tempFile);
            Assert.NotNull(loaded);

            Assert.Equal(original.Request!.Origin, loaded.Request!.Origin);
            Assert.Equal(original.Request.Destination, loaded.Request.Destination);
            Assert.Equal(original.Request.DepartureTime, loaded.Request.DepartureTime);
            Assert.Equal(original.Request.TravelTime, loaded.Request.TravelTime);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromFile_Nonexistent_ReturnsNull()
    {
        var result = ScenarioJson.LoadFromFile("nonexistent_path.json");
        Assert.Null(result);
    }
}
