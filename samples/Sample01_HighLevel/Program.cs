using TrajectoryCalculator;

var planner = new TrajectoryPlanner();
var input = SampleScenarioFactory.CreateSingleTemplate();

var result = planner.Calculate(input);

Console.WriteLine($"=== Earth → Mars (High-level API) ===");
Console.WriteLine(result.ConsoleSummary);
Console.WriteLine();
Console.WriteLine($"Transfer ΔV: {result.Transfer?.DVTotal:F1} m/s");
