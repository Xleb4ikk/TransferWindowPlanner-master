using TrajectoryCalculator;

// Build the Earth and Mars orbital bodies from the JPL catalog
var earth = SolarSystemCatalog.CreateOrbitalBody("Earth");
var mars = SolarSystemCatalog.CreateOrbitalBody("Mars");

var centralBody = new CentralBody("Sun", SolarSystemCatalog.SunGravitationalParameter);

// Calculate a transfer using the low-level API directly
var transfer = TransferCalculator.CalculateTransfer(
    earth, mars, centralBody,
    departureTime: SolarSystemCatalog.ToJ2000Seconds(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)),
    travelTime: 220 * SolarSystemCatalog.SecondsPerDay,
    departureParkingOrbitPeriapsisAltitude: 200_000.0,
    departureParkingOrbitApoapsisAltitude: 200_000.0,
    arrivalParkingOrbitAltitude: 250_000.0,
    arrivalManeuverMode: "circular-capture",
    useAerobraking: false,
    longWayOverride: false);

Console.WriteLine($"=== Earth → Mars (Low-level API) ===");
Console.WriteLine($"Transfer ΔV:       {transfer?.DVTotal:F1} m/s");
Console.WriteLine($"Ejection ΔV:       {transfer?.DVEjection:F1} m/s");
Console.WriteLine($"Insertion ΔV:      {transfer?.DVInjection:F1} m/s");
Console.WriteLine($"Departure time:    {SolarSystemCatalog.FromJ2000Seconds(transfer?.DepartureTime ?? 0):yyyy-MM-dd HH:mm:ss} UTC");
Console.WriteLine($"Travel time:       {transfer?.TravelTime / SolarSystemCatalog.SecondsPerDay:F1} days");
