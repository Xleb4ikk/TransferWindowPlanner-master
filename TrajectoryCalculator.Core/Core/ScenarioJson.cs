using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrajectoryCalculator;

/// <summary>
/// JSON serialization helpers for scenario inputs.
/// </summary>
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

    /// <summary>
    /// Loads and deserializes a scenario from a JSON file.
    /// </summary>
    /// <param name="inputPath">Path to the JSON file.</param>
    /// <returns>The deserialized scenario, or <c>null</c> if the file was not found.</returns>
    public static ScenarioInput? LoadFromFile(string inputPath)
    {
        try
        {
            var json = File.ReadAllText(inputPath);
            return JsonSerializer.Deserialize<ScenarioInput>(json, Options);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public static string Serialize(ScenarioInput scenario)
    {
        return JsonSerializer.Serialize(scenario, Options);
    }

    /// <summary>
    /// Serializes and saves a scenario to a JSON file.
    /// </summary>
    public static void SaveToFile(ScenarioInput scenario, string outputPath)
    {
        File.WriteAllText(outputPath, Serialize(scenario) + Environment.NewLine);
    }
}
