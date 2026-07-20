using TrajectoryCalculator;

if (args.Length == 1)
{
    var fileScenario = ScenarioJson.LoadFromFile(args[0]);
    if (fileScenario is null)
    {
        Console.Error.WriteLine($"Failed to load '{args[0]}'.");
        return 1;
    }
    var fileResult = new TrajectoryPlanner().Calculate(fileScenario);
    Console.WriteLine(fileResult.ConsoleSummary);
    return 0;
}

ScenarioInput? scenario = null;
var planner = new TrajectoryPlanner();

while (true)
{
    Console.Clear();
    Console.WriteLine("=====================================");
    Console.WriteLine("  Trajectory Calculator Demo");
    Console.WriteLine("=====================================");
    Console.WriteLine();
    Console.WriteLine("Current mission");
    Console.WriteLine("-------------------------------------");
    Console.WriteLine(scenario is null ? "  (none)" : FormatMission(scenario));
    Console.WriteLine("-------------------------------------");
    Console.WriteLine();
    Console.WriteLine("  1  New mission");
    Console.WriteLine("  2  Load JSON");
    Console.WriteLine("  3  Save JSON");
    Console.WriteLine();
    Console.WriteLine("  4  Change departure");
    Console.WriteLine("  5  Change travel time");
    Console.WriteLine("  6  Change parking orbit");
    Console.WriteLine();
    Console.WriteLine("  7  Calculate");
    Console.WriteLine("  8  Reset template (Earth -> Mars)");
    Console.WriteLine("  9  Help");
    Console.WriteLine("  0  Exit");
    Console.WriteLine();
    Console.Write("> ");

    switch (Console.ReadLine()?.Trim())
    {
        case "1": scenario = NewMission(); break;
        case "2": scenario = LoadJson(); break;
        case "3": SaveJson(scenario); break;
        case "4": scenario = ChangeDeparture(scenario); break;
        case "5": scenario = ChangeTravelTime(scenario); break;
        case "6": scenario = ChangeParkingOrbit(scenario); break;
        case "7": Calculate(scenario, planner); break;
        case "8": scenario = SampleScenarioFactory.CreateSingleTemplate(); break;
        case "9": ShowHelp(); break;
        case "0": return 0;
    }
}

static ScenarioInput NewMission()
{
    var planets = SolarSystemCatalog.PlanetNames;

    Console.WriteLine();
    Console.WriteLine("Origin:");
    for (int i = 0; i < planets.Count; i++)
        Console.WriteLine($"  {i + 1}. {planets[i]}");
    Console.Write("> ");
    var origin = planets[int.Parse(Console.ReadLine()!) - 1];

    Console.WriteLine();
    Console.WriteLine("Destination:");
    for (int i = 0; i < planets.Count; i++)
        Console.WriteLine($"  {i + 1}. {planets[i]}");
    string destination;
    while (true)
    {
        Console.Write("> ");
        destination = planets[int.Parse(Console.ReadLine()!) - 1];
        if (!destination.Equals(origin, StringComparison.OrdinalIgnoreCase))
            break;
        Console.WriteLine("  Destination must differ from origin.");
    }

    Console.Write("Departure year [{0}]: ", 2026);
    var y = Console.ReadLine(); int year = string.IsNullOrEmpty(y) ? 2026 : int.Parse(y);
    Console.Write("Departure month [{0}]: ", 11);
    var m = Console.ReadLine(); int month = string.IsNullOrEmpty(m) ? 11 : int.Parse(m);
    Console.Write("Departure day [{0}]: ", 12);
    var d = Console.ReadLine(); int day = string.IsNullOrEmpty(d) ? 12 : int.Parse(d);
    Console.Write("Travel days [{0}]: ", 220);
    var td = Console.ReadLine(); double travelDays = string.IsNullOrEmpty(td) ? 220 : double.Parse(td);
    Console.Write("Parking orbit altitude (km) [{0}]: ", 200);
    var ap = Console.ReadLine(); double altKm = string.IsNullOrEmpty(ap) ? 200 : double.Parse(ap);

    var dt = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);

    return new ScenarioInput
    {
        Calendar = SolarSystemCatalog.CreateUtcCalendarInput(),
        CentralBody = SolarSystemCatalog.CreateSunInput(),
        Bodies = [.. SolarSystemCatalog.CreateSelectedBodies(origin, destination)],
        Request = new CalculationRequest
        {
            Origin = origin,
            Destination = destination,
            Mode = "single",
            DepartureTime = SolarSystemCatalog.ToJ2000Seconds(dt),
            TravelTime = travelDays * SolarSystemCatalog.SecondsPerDay,
            DepartureParkingOrbitAltitude = altKm * 1000.0,
            ArrivalManeuverMode = "circular-capture"
        }
    };
}

static ScenarioInput? LoadJson()
{
    Console.WriteLine();
    Console.Write("Path: ");
    var path = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(path)) return null;
    var loaded = ScenarioJson.LoadFromFile(path);
    if (loaded is null)
        Console.WriteLine("  Failed to load file.");
    else
        Console.WriteLine("  Loaded.");
    Pause();
    return loaded;
}

static void SaveJson(ScenarioInput? scenario)
{
    if (scenario is null) { Console.WriteLine("  No mission defined."); Pause(); return; }
    Console.WriteLine();
    Console.Write("Path: ");
    var path = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(path)) return;
    ScenarioJson.SaveToFile(scenario, path);
    Console.WriteLine("  Saved.");
    Pause();
}

static ScenarioInput? ChangeDeparture(ScenarioInput? scenario)
{
    if (scenario is null) { Console.WriteLine("  No mission defined."); Pause(); return null; }
    var req = scenario.Request;
    var current = req.DepartureTime.HasValue
        ? SolarSystemCatalog.FromJ2000Seconds(req.DepartureTime.Value)
        : new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    Console.Write("New departure year [{0}]: ", current.Year);
    var y = Console.ReadLine(); int year = string.IsNullOrEmpty(y) ? current.Year : int.Parse(y);
    Console.Write("New departure month [{0}]: ", current.Month);
    var m = Console.ReadLine(); int month = string.IsNullOrEmpty(m) ? current.Month : int.Parse(m);
    Console.Write("New departure day [{0}]: ", current.Day);
    var d = Console.ReadLine(); int day = string.IsNullOrEmpty(d) ? current.Day : int.Parse(d);

    req.DepartureTime = SolarSystemCatalog.ToJ2000Seconds(
        new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero));
    Console.WriteLine("  Updated.");
    Pause();
    return scenario;
}

static ScenarioInput? ChangeTravelTime(ScenarioInput? scenario)
{
    if (scenario is null) { Console.WriteLine("  No mission defined."); Pause(); return null; }
    var req = scenario.Request;
    var currentDays = req.TravelTime.HasValue ? req.TravelTime.Value / SolarSystemCatalog.SecondsPerDay : 220;

    Console.Write("New travel days [{0:F0}]: ", currentDays);
    var t = Console.ReadLine();
    req.TravelTime = (string.IsNullOrEmpty(t) ? currentDays : double.Parse(t)) * SolarSystemCatalog.SecondsPerDay;
    Console.WriteLine("  Updated.");
    Pause();
    return scenario;
}

static ScenarioInput? ChangeParkingOrbit(ScenarioInput? scenario)
{
    if (scenario is null) { Console.WriteLine("  No mission defined."); Pause(); return null; }
    var req = scenario.Request;
    var currentAlt = req.ResolveDepartureOrbitPeriapsisAltitude() / 1000.0;

    Console.Write("New parking orbit altitude (km) [{0:F0}]: ", currentAlt);
    var a = Console.ReadLine();
    req.DepartureParkingOrbitAltitude = (string.IsNullOrEmpty(a) ? currentAlt : double.Parse(a)) * 1000.0;
    Console.WriteLine("  Updated.");
    Pause();
    return scenario;
}

static void Calculate(ScenarioInput? scenario, TrajectoryPlanner planner)
{
    if (scenario is null) { Console.WriteLine("  No mission defined."); Pause(); return; }

    Console.WriteLine();
    Console.WriteLine("  Calculating...");

    var result = planner.Calculate(scenario);

    Console.WriteLine();
    Console.WriteLine("==============================");
    Console.WriteLine(result.ConsoleSummary);
    Console.WriteLine("==============================");

    Console.WriteLine();
    Console.WriteLine("Save result? (1 yes / 2 no)");
    Console.Write("> ");
    if (Console.ReadLine()?.Trim() == "1")
    {
        Console.Write("Path (summary.txt): ");
        var path = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(path)) path = "summary.txt";
        File.WriteAllText(path, result.ConsoleSummary);
        Console.WriteLine($"  Saved to {path}.");
    }

    Pause();
}

static void ShowHelp()
{
    Console.Clear();
    Console.WriteLine("=====================================");
    Console.WriteLine("  Trajectory Calculator Demo");
    Console.WriteLine("=====================================");
    Console.WriteLine();
    Console.WriteLine("This program demonstrates the public API of the");
    Console.WriteLine("TrajectoryCalculator library.");
    Console.WriteLine();
    Console.WriteLine("Public API types used:");
    Console.WriteLine("  TrajectoryPlanner    - high-level calculation");
    Console.WriteLine("  ScenarioInput        - mission parameters");
    Console.WriteLine("  ScenarioJson         - load/save JSON files");
    Console.WriteLine("  ExecutionResult      - calculation results");
    Console.WriteLine("  TransferDetails      - delta-V breakdown");
    Console.WriteLine("  SolarSystemCatalog   - planet data and helpers");
    Console.WriteLine("  SampleScenarioFactory- template scenarios");
    Console.WriteLine();
    Console.WriteLine("Command line:");
    Console.WriteLine("  MissionPlannerCli <file.json>");
    Console.WriteLine("  Loads and calculates a scenario directly.");
    Console.WriteLine();
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}

static string FormatMission(ScenarioInput s)
{
    var req = s.Request;
    var dep = req.DepartureTime.HasValue
        ? SolarSystemCatalog.FromJ2000Seconds(req.DepartureTime.Value).ToString("yyyy-MM-dd")
        : "(not set)";
    var travel = req.TravelTime.HasValue
        ? $"{req.TravelTime.Value / SolarSystemCatalog.SecondsPerDay:F0} days"
        : "(not set)";
    var alt = $"{req.ResolveDepartureOrbitPeriapsisAltitude() / 1000.0:F0} km";
    return $"  {req.Origin} -> {req.Destination}\n  Departure: {dep}\n  Travel: {travel}\n  Parking orbit: {alt}";
}

static void Pause()
{
    Console.WriteLine();
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}
