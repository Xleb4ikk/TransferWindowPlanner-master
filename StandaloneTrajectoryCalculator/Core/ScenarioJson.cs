using System.Text.Json;
using System.Text.Json.Serialization;

namespace StandaloneTrajectoryCalculator;

public static class ScenarioJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ScenarioInput? LoadFromFile(string inputPath)
    {
        var json = File.ReadAllText(inputPath);
        return JsonSerializer.Deserialize<ScenarioInput>(json, Options);
    }

    public static string Serialize(ScenarioInput scenario)
    {
        return JsonSerializer.Serialize(scenario, Options);
    }

    public static void SaveToFile(ScenarioInput scenario, string outputPath)
    {
        File.WriteAllText(outputPath, Serialize(scenario) + Environment.NewLine);
    }
}
