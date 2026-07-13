using TrajectoryCalculator;

var planner = new TrajectoryPlanner();
var inputPath = Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..",
    "StandaloneTrajectoryCalculator", "sample-single.json");
inputPath = Path.GetFullPath(inputPath);

var result = planner.CalculateFromFile(inputPath);

Console.WriteLine($"=== Earth → Mars from file ({inputPath}) ===");
Console.WriteLine(result.ConsoleSummary);
Console.WriteLine();
Console.WriteLine($"Written files:");
foreach (var file in result.WrittenFiles)
    Console.WriteLine($"  {file}");
