namespace StandaloneTrajectoryCalculator;

public sealed class ScenarioInput
{
    public CalendarInput Calendar { get; set; } = new();
    public CentralBodyInput CentralBody { get; set; } = new();
    public List<BodyInput> Bodies { get; set; } = [];
    public CalculationRequest Request { get; set; } = new();
}

public sealed class CalendarInput
{
    public string Kind { get; set; } = "custom";
    public string? EpochUtc { get; set; }
    public int EpochYear { get; set; } = 1;
    public int EpochDayOfYear { get; set; } = 1;
    public int DaysPerYear { get; set; } = 426;
    public int HoursPerDay { get; set; } = 6;
    public int MinutesPerHour { get; set; } = 60;
    public int SecondsPerMinute { get; set; } = 60;
}

public sealed class CentralBodyInput
{
    public string Name { get; set; } = "CentralBody";
    public double GravitationalParameter { get; set; }
}

public sealed class BodyInput
{
    public string Name { get; set; } = string.Empty;
    public double GravitationalParameter { get; set; }
    public double Radius { get; set; }
    public double? SphereOfInfluence { get; set; }
    public double? RotationPeriodSeconds { get; set; }  // Planet rotation period in seconds
    public OrbitInput Orbit { get; set; } = new();
}

public sealed class OrbitInput
{
    public double SemiMajorAxis { get; set; }
    public double Eccentricity { get; set; }
    public double InclinationDeg { get; set; }
    public double LongitudeOfAscendingNodeDeg { get; set; }
    public double ArgumentOfPeriapsisDeg { get; set; }
    public double MeanAnomalyAtEpochDeg { get; set; }
    public double Epoch { get; set; }
    public double? SemiMajorAxisRate { get; set; }
    public double? EccentricityRate { get; set; }
    public double? InclinationRateDegPerCentury { get; set; }
    public double? LongitudeOfAscendingNodeRateDegPerCentury { get; set; }
    public double? LongitudeOfPeriapsisDeg { get; set; }
    public double? LongitudeOfPeriapsisRateDegPerCentury { get; set; }
    public double? MeanLongitudeDeg { get; set; }
    public double? MeanLongitudeRateDegPerCentury { get; set; }
    public double? MeanAnomalyQuadraticTermDegPerCentury2 { get; set; }
    public double? MeanAnomalyCosineTermDeg { get; set; }
    public double? MeanAnomalySineTermDeg { get; set; }
    public double? MeanAnomalyFrequencyDegPerCentury { get; set; }
}

public sealed class CalculationRequest
{
    public string Mode { get; set; } = "single";
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public double? DepartureTime { get; set; }
    public double? TravelTime { get; set; }
    public double? DepartureWindowStart { get; set; }
    public double? DepartureWindowEnd { get; set; }
    public double? TravelTimeMin { get; set; }
    public double? TravelTimeMax { get; set; }
    public int DepartureSteps { get; set; } = 80;
    public int TravelTimeSteps { get; set; } = 80;
    public double DepartureParkingOrbitAltitude { get; set; }
    public double? DepartureParkingOrbitPeriapsisAltitude { get; set; }
    public double? DepartureParkingOrbitApoapsisAltitude { get; set; }
    public double? ArrivalParkingOrbitAltitude { get; set; }
    public string? ArrivalManeuverMode { get; set; } = "circular-capture";
    public bool UseAerobraking { get; set; }
    public bool? LongWay { get; set; }
    public string? CsvOutputPath { get; set; }
    public string? ResultOutputPath { get; set; }
    
    // Параметры расчёта подъёма на орбиту
    public LaunchConfiguration? Launch { get; set; }

    public double ResolveDepartureOrbitPeriapsisAltitude()
    {
        return DepartureParkingOrbitPeriapsisAltitude ?? DepartureParkingOrbitAltitude;
    }

    public double ResolveDepartureOrbitApoapsisAltitude()
    {
        return DepartureParkingOrbitApoapsisAltitude ?? DepartureParkingOrbitAltitude;
    }
}

/// <summary>
/// Configuration for orbital launch calculations
/// </summary>
public sealed class LaunchConfiguration
{
    /// <summary>
    /// Enable launch delta-V calculations
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Calculation mode: "detailed" for full simulation, "quick" for estimation
    /// </summary>
    public string Mode { get; set; } = "quick";
    
    /// <summary>
    /// Rocket initial mass in kg
    /// </summary>
    public double RocketInitialMass { get; set; } = 500000;
    
    /// <summary>
    /// Rocket thrust in Newtons
    /// </summary>
    public double RocketThrust { get; set; } = 7000000;
    
    /// <summary>
    /// Rocket specific impulse in seconds
    /// </summary>
    public double RocketIsp { get; set; } = 310;
    
    /// <summary>
    /// Launch latitude in degrees (0 = equator)
    /// </summary>
    public double LaunchLatitude { get; set; } = 0.0;

    /// <summary>
    /// Launch site east longitude in degrees in the inertial reference used by the solver
    /// at t = 0 (J2000). Default is 0.
    /// </summary>
    public double LaunchLongitude { get; set; } = 0.0;
    
    /// <summary>
    /// Target orbit inclination in degrees
    /// </summary>
    public double TargetInclination { get; set; } = 0.0;
    
}
