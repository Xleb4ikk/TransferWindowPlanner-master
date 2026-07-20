using TrajectoryCalculator;

static double GetParkingAltitude(string planetName) => planetName.ToLowerInvariant() switch
{
    "mercury" or "venus" or "earth" or "mars" => 300_000,
    "jupiter" or "saturn" or "uranus" or "neptune" => 50_000_000,
    _ => 200_000
};

var planets = SolarSystemCatalog.PlanetNames;
var outputDir = @"C:\missions";
Directory.CreateDirectory(outputDir);

var manifest = new List<string>();
int index = 0;

foreach (var origin in planets)
foreach (var dest in planets.Where(d => d != origin))
for (int w = 0; w < 20; w++)
{
    var y0 = 2000 + (int)(w * 2.5);
    var y1 = y0 + 3;
    if (y1 > 2050) y1 = 2050;
    if (y0 >= 2050) continue;

    var scenario = new ScenarioInput
    {
        Calendar = SolarSystemCatalog.CreateUtcCalendarInput(),
        CentralBody = SolarSystemCatalog.CreateSunInput(),
        Bodies = [.. SolarSystemCatalog.CreateSelectedBodies(origin, dest)],
        Request = new CalculationRequest
        {
            Origin = origin,
            Destination = dest,
            Mode = "porkchop",
            DepartureWindowStart = SolarSystemCatalog.ToJ2000Seconds(
                new DateTimeOffset(y0, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            DepartureWindowEnd = SolarSystemCatalog.ToJ2000Seconds(
                new DateTimeOffset(y1, 12, 31, 0, 0, 0, TimeSpan.Zero)),
            TravelTimeMin = 30 * 86400,
            TravelTimeMax = 10000 * 86400,
            DepartureSteps = 60,
            TravelTimeSteps = 500,
            DepartureParkingOrbitAltitude = GetParkingAltitude(origin),
            ArrivalParkingOrbitAltitude = GetParkingAltitude(dest),
            ArrivalManeuverMode = "circular-capture"
        }
    };

    var fileName = $"{origin}-to-{dest}-{y0}-{y1}.json";
    var filePath = Path.Combine(outputDir, fileName);
    ScenarioJson.SaveToFile(scenario, filePath);
    manifest.Add(fileName);
    index++;
}

var manifestJson = System.Text.Json.JsonSerializer.Serialize(
    manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(outputDir, "manifest.json"), manifestJson);
Console.WriteLine($"Generated {index} porkchop scenarios -> {outputDir}");
