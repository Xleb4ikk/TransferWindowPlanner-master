using System.Globalization;
using TrajectoryCalculator;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            if (args[0].Equals("template", StringComparison.OrdinalIgnoreCase))
            {
                return WriteTemplate(args);
            }

            if (args[0].Equals("dataset", StringComparison.OrdinalIgnoreCase))
            {
                return WriteDataset(args);
            }

            var inputPath = ResolveInputPath(args);
            if (inputPath is null)
            {
                PrintUsage();
                return 1;
            }

            var scenario = LoadScenario(inputPath);
            var result = ScenarioExecutor.Execute(scenario, inputPath, ScenarioJson.Options);

            Console.WriteLine(result.ConsoleSummary);

            if (result.WrittenFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Written files:");
                foreach (var file in result.WrittenFiles)
                {
                    Console.WriteLine($"  {file}");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static ScenarioInput LoadScenario(string inputPath)
    {
        var scenario = ScenarioJson.LoadFromFile(inputPath);
        if (scenario is null)
        {
            throw new InvalidOperationException("Input JSON could not be parsed.");
        }

        return scenario;
    }

    private static string? ResolveInputPath(string[] args)
    {
        if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                return null;
            }

            return Path.GetFullPath(args[1]);
        }

        return Path.GetFullPath(args[0]);
    }

    private static int WriteTemplate(string[] args)
    {
        var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "porkchop";
        var outputPath = args.Length > 2
            ? Path.GetFullPath(args[2])
            : Path.GetFullPath($"sample-{mode}.json");

        ScenarioInput template = mode switch
        {
            "single" => SampleScenarioFactory.CreateSingleTemplate(),
            "porkchop" => SampleScenarioFactory.CreatePorkchopTemplate(),
            _ => throw new InvalidOperationException("Template mode must be 'single' or 'porkchop'.")
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ScenarioJson.SaveToFile(template, outputPath);

        Console.WriteLine($"Template written to {outputPath}");
        return 0;
    }

    private static int WriteDataset(string[] args)
    {
        var datasetType = args.Length > 1 ? args[1].ToLowerInvariant() : "fuel";
        if (!datasetType.Equals("fuel", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dataset type must be 'fuel'.");
        }

        var outputDirectory = args.Length > 2
            ? Path.GetFullPath(args[2])
            : Path.GetFullPath("data/datasets/fuel-dataset");
        var missionCount = args.Length > 3
            ? ParsePositiveInt(args[3], "missionCount")
            : 300;
        var seed = args.Length > 4
            ? int.Parse(args[4], CultureInfo.InvariantCulture)
            : 12345;
        var maxArchitecturesPerMissionOutput = args.Length > 5
            ? ParseNonNegativeInt(args[5], "topArchitecturesPerMission")
            : 32;

        var result = FuelDatasetGenerator.Generate(new FuelDatasetOptions
        {
            OutputDirectory = outputDirectory,
            MissionCount = missionCount,
            Seed = seed,
            MaxArchitecturesPerMissionOutput = maxArchitecturesPerMissionOutput
        });

        Console.WriteLine("Fuel dataset generated");
        Console.WriteLine($"  Output directory:   {result.OutputDirectory}");
        Console.WriteLine($"  Mission samples:    {result.MissionCount}");
        Console.WriteLine($"  Architecture rows:  {result.ArchitectureCount}");
        Console.WriteLine($"  Missions CSV:       {result.MissionDatasetPath}");
        Console.WriteLine($"  Architectures CSV:  {result.ArchitectureDatasetPath}");
        Console.WriteLine($"  Metadata JSON:      {result.MetadataPath}");
        return 0;
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{name} must be a positive integer.");
        }

        return parsed;
    }

    private static int ParseNonNegativeInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw new InvalidOperationException($"{name} must be a non-negative integer.");
        }

        return parsed;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("StandaloneTrajectoryCalculator");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  StandaloneTrajectoryCalculator <input.json>");
        Console.WriteLine("  StandaloneTrajectoryCalculator run <input.json>");
        Console.WriteLine("  StandaloneTrajectoryCalculator template [single|porkchop] [output.json]");
        Console.WriteLine("  StandaloneTrajectoryCalculator dataset fuel [outputDir] [missionCount] [seed] [topArchitecturesPerMission]");
        Console.WriteLine();
        Console.WriteLine("The input JSON contains the central body, orbital elements for bodies,");
        Console.WriteLine("calendar settings, and either a single transfer request or a porkchop scan.");
        Console.WriteLine();
        Console.WriteLine($"Current culture: {CultureInfo.CurrentCulture.Name}");
    }
}
