using System.Globalization;
using System.Text;

namespace TrajectoryCalculator;

/// <summary>
/// Computed interplanetary transfer details: delta-V, positions, velocities, and maneuver parameters.
/// </summary>
public sealed class TransferDetails
{
    /// <summary>Name of the departure body.</summary>
    public string OriginName { get; set; } = string.Empty;
    /// <summary>Name of the destination body.</summary>
    public string DestinationName { get; set; } = string.Empty;
    /// <summary>Departure time in J2000 seconds.</summary>
    public double DepartureTime { get; set; }
    /// <summary>Interplanetary travel time in seconds.</summary>
    public double TravelTime { get; set; }
    /// <summary>Whether the transfer goes the long way around.</summary>
    public bool LongWay { get; set; }

    public Vector3D OriginPositionAtDeparture { get; set; }
    public Vector3D DestinationPositionAtArrival { get; set; }
    public Vector3D OriginVelocity { get; set; }
    public Vector3D TransferInitialVelocity { get; set; }
    public Vector3D TransferFinalVelocity { get; set; }
    public Vector3D DestinationVelocity { get; set; }

    public double OriginVesselOrbitalSpeed { get; set; }
    public double DestinationVesselOrbitalSpeed { get; set; }
    public double OriginBodyRadius { get; set; }
    public double OriginOrbitPeriapsisRadius { get; set; }
    public double OriginOrbitApoapsisRadius { get; set; }

    public Vector3D EjectionDeltaVector { get; set; }
    public Vector3D InjectionDeltaVector { get; set; }

    /// <summary>Phase angle between origin and destination at departure (radians).</summary>
    public double PhaseAngle { get; set; }
    /// <summary>Total transfer angle (radians).</summary>
    public double TransferAngle { get; set; }
    /// <summary>Minimum angular separation between bodies (radians).</summary>
    public double DepartureSeparation => Math.Min(Math.Abs(PhaseAngle), LambertSolver.TwoPi - Math.Abs(PhaseAngle));
    public double EjectionInclination { get; set; }
    public double InsertionInclination { get; set; }
    public double EjectionDVNormal { get; set; }
    public double EjectionDVPrograde { get; set; }
    public double EjectionHeading { get; set; }
    public double EjectionAngle { get; set; }
    public bool EjectionAngleIsRetrograde { get; set; }
    public bool HasDetailedEjectionAnalysis { get; private set; }
    /// <summary>Arrival maneuver mode (e.g. "circular-capture", "aerobraking", "flyby").</summary>
    public string ArrivalManeuverMode { get; set; } = "none";
    public double? ArrivalCapturePeriapsisRadius { get; set; }
    public double? ArrivalCaptureApoapsisRadius { get; set; }

    /// <summary>Ejection delta-V magnitude in m/s.</summary>
    public double DVEjection => EjectionDeltaVector.Magnitude;
    /// <summary>Injection (capture) delta-V magnitude in m/s.</summary>
    public double DVInjection => InjectionDeltaVector.Magnitude;
    /// <summary>Total mission delta-V in m/s.</summary>
    public double DVTotal => DVEjection + DVInjection;
    public Vector3D EjectionVector => TransferInitialVelocity - OriginVelocity;
    public string EjectionAngleText =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{EjectionAngle * LambertSolver.Rad2Deg:0.00} deg {(EjectionAngleIsRetrograde ? "to retrograde" : "to prograde")}");

    /// <summary>
    /// Computes detailed ejection parameters (heading, angle, prograde/normal components).
    /// </summary>
    public void CalculateEjectionValues(OrbitalBody origin, OrbitalBody destination)
    {
        if (!origin.SphereOfInfluence.HasValue)
        {
            throw new InvalidOperationException($"Body '{origin.Name}' needs sphereOfInfluence for ejection analysis.");
        }

        var mu = origin.GravitationalParameter;
        var sphereOfInfluence = origin.SphereOfInfluence.Value;
        var velocityAtSoi = EjectionVector.Magnitude;
        var initialOrbitRadius = OriginOrbitPeriapsisRadius > 0
            ? OriginOrbitPeriapsisRadius
            : mu / (OriginVesselOrbitalSpeed * OriginVesselOrbitalSpeed);
        var velocityAtPeriapsis = Math.Sqrt(
            velocityAtSoi * velocityAtSoi +
            2 * mu / initialOrbitRadius -
            2 * mu / sphereOfInfluence);

        EjectionDVNormal = velocityAtPeriapsis * Math.Sin(EjectionInclination);
        EjectionDVPrograde = velocityAtPeriapsis * Math.Cos(EjectionInclination) - OriginVesselOrbitalSpeed;
        EjectionHeading = Math.Atan2(EjectionDVPrograde, EjectionDVNormal);

        var eccentricity = initialOrbitRadius * velocityAtPeriapsis * velocityAtPeriapsis / mu - 1;
        var semiMajorAxis = initialOrbitRadius / (1 - eccentricity);
        var theta = Math.Acos((semiMajorAxis * (1 - eccentricity * eccentricity) - sphereOfInfluence) / (eccentricity * sphereOfInfluence));
        theta += Math.Asin(velocityAtPeriapsis * initialOrbitRadius / (velocityAtSoi * sphereOfInfluence));

        EjectionAngle = CalculateEjectionAngle(EjectionDeltaVector, theta, OriginVelocity.Normalized);

        if (destination.Orbit.SemiMajorAxis < origin.Orbit.SemiMajorAxis)
        {
            EjectionAngleIsRetrograde = true;
            EjectionAngle -= Math.PI;
            if (EjectionAngle < 0)
            {
                EjectionAngle += LambertSolver.TwoPi;
            }
        }
        else
        {
            EjectionAngleIsRetrograde = false;
        }

        HasDetailedEjectionAnalysis = true;
    }

    /// <summary>
    /// Formats a human-readable summary of the transfer using the given calendar.
    /// </summary>
    public string ToSummaryText(MissionCalendar calendar)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{OriginName} -> {DestinationName}");
        builder.AppendLine($"Long-way transfer: {(LongWay ? "yes" : "no")}");
        builder.AppendLine($"Depart at:        {calendar.FormatDate(DepartureTime)}");
        builder.AppendLine($"Departure UT:     {DepartureTime:0.###} s");
        builder.AppendLine($"Travel time:      {calendar.FormatDuration(TravelTime)}");
        builder.AppendLine($"Travel UT:        {TravelTime:0.###} s");
        builder.AppendLine($"Arrive at:        {calendar.FormatDate(DepartureTime + TravelTime)}");
        builder.AppendLine($"Arrival UT:       {DepartureTime + TravelTime:0.###} s");
        builder.AppendLine($"Phase angle:      {PhaseAngle * LambertSolver.Rad2Deg:0.00} deg ({DepartureSeparation * LambertSolver.Rad2Deg:0.00} deg separation)");
        builder.AppendLine($"Transfer angle:   {TransferAngle * LambertSolver.Rad2Deg:0.00} deg");
        builder.AppendLine($"Ejection inc.:    {EjectionInclination * LambertSolver.Rad2Deg:0.00} deg");

        if (OriginOrbitPeriapsisRadius > 0)
        {
            var periapsisAltitudeKm = Math.Max(0.0, OriginOrbitPeriapsisRadius - OriginBodyRadius) / 1000.0;
            var apoapsisAltitudeKm = Math.Max(0.0, OriginOrbitApoapsisRadius - OriginBodyRadius) / 1000.0;
            builder.AppendLine($"Departure orbit:  {periapsisAltitudeKm:0.0} x {apoapsisAltitudeKm:0.0} km");
        }

        builder.AppendLine($"Ejection dV:      {DVEjection:0.0} m/s");

        if (HasDetailedEjectionAnalysis)
        {
            builder.AppendLine($"Ejection angle:   {EjectionAngleText}");
            builder.AppendLine($"Prograde dV:      {EjectionDVPrograde:0.0} m/s");
            builder.AppendLine($"Normal dV:        {EjectionDVNormal:0.0} m/s");
            builder.AppendLine($"Heading:          {EjectionHeading * LambertSolver.Rad2Deg:0.00} deg");
        }

        if (!string.IsNullOrWhiteSpace(ArrivalManeuverMode) && !ArrivalManeuverMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"Arrival mode:     {FormatArrivalMode()}");
            if (ArrivalCapturePeriapsisRadius.HasValue)
            {
                builder.AppendLine($"Capture periapsis:{ArrivalCapturePeriapsisRadius.Value / 1000.0,9:0.0} km");
            }
            if (ArrivalCaptureApoapsisRadius.HasValue)
            {
                builder.AppendLine($"Capture apoapsis: {ArrivalCaptureApoapsisRadius.Value / 1000.0:0.0} km");
            }
        }

        builder.AppendLine($"Insertion inc.:   {InsertionInclination * LambertSolver.Rad2Deg:0.00} deg");
        builder.AppendLine($"Insertion dV:     {DVInjection:0.0} m/s");
        builder.AppendLine($"Total dV:         {DVTotal:0.0} m/s");
        return builder.ToString().TrimEnd();
    }

    private string FormatArrivalMode()
    {
        return ArrivalManeuverMode.ToLowerInvariant() switch
        {
            "elliptic-capture" => "high-elliptic capture",
            "circular-capture" => "circular capture",
            "aerobraking" => "aerobraking",
            "flyby" => "flyby",
            "ignore-arrival-burn" => "ignore arrival burn",
            _ => ArrivalManeuverMode
        };
    }

    private static double CalculateEjectionAngle(Vector3D velocityAtSoi, double theta, Vector3D prograde)
    {
        var direction = velocityAtSoi.Normalized;
        var ax = direction.X;
        var ay = direction.Y;
        var az = direction.Z;

        if (Math.Abs(ay) < 1e-12)
        {
            throw new InvalidOperationException("Ejection angle calculation is singular for this geometry.");
        }

        var cosTheta = Math.Cos(theta);
        var g = -ax / ay;
        var a = 1 + g * g;
        var b = 2 * g * cosTheta / ay;
        var c = cosTheta * cosTheta / (ay * ay) - 1;

        double q;
        if (b < 0)
        {
            q = -0.5 * (b - Math.Sqrt(b * b - 4 * a * c));
        }
        else
        {
            q = -0.5 * (b + Math.Sqrt(b * b - 4 * a * c));
        }

        var vx = q / a;
        var vy = g * vx + cosTheta / ay;
        var planarVector = new Vector3D(vx, vy, 0);

        if (Math.Sign(Vector3D.Cross(planarVector, new Vector3D(ax, ay, az)).Z) != Math.Sign(Math.PI - theta))
        {
            vx = c / q;
            vy = g * vx + cosTheta / ay;
            planarVector = new Vector3D(vx, vy, 0);
        }

        var planarPrograde = new Vector3D(prograde.X, prograde.Y, 0).Normalized;
        var angle = Math.Acos(Math.Clamp(Vector3D.Dot(planarVector.Normalized, planarPrograde), -1.0, 1.0));
        return Vector3D.Cross(planarVector, planarPrograde).Z < 0
            ? LambertSolver.TwoPi - angle
            : angle;
    }
}

public sealed class PorkchopPoint
{
    public double DepartureTime { get; set; }
    public double TravelTime { get; set; }
    public double? TotalDeltaV { get; set; }
    public bool? LongWay { get; set; }
}

public sealed class PorkchopWindow
{
    public double DepartureStart { get; set; }
    public double DepartureEnd { get; set; }
    public double TravelTimeMin { get; set; }
    public double TravelTimeMax { get; set; }
    public int DepartureSteps { get; set; }
    public int TravelTimeSteps { get; set; }
}

/// <summary>
/// Result of a porkchop scan: grid, best transfer, and window parameters.
/// </summary>
public sealed class PorkchopResult
{
    public required PorkchopWindow Window { get; init; }
    public required TransferDetails BestTransfer { get; init; }
    public required List<PorkchopPoint> Points { get; init; }
    public required int ValidPoints { get; init; }
    public required int InvalidPoints { get; init; }
    public required double HohmannTimeOfFlight { get; init; }
    public required double SynodicPeriod { get; init; }

    public string ToSummaryText(MissionCalendar calendar)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Porkchop scan completed");
        builder.AppendLine($"Departure window: {calendar.FormatDate(Window.DepartureStart)} -> {calendar.FormatDate(Window.DepartureEnd)}");
        builder.AppendLine($"Travel window:    {calendar.FormatDuration(Window.TravelTimeMin)} -> {calendar.FormatDuration(Window.TravelTimeMax)}");
        builder.AppendLine($"Grid size:        {Window.DepartureSteps} x {Window.TravelTimeSteps}");
        builder.AppendLine($"Valid points:     {ValidPoints}");
        builder.AppendLine($"Invalid points:   {InvalidPoints}");
        builder.AppendLine($"Hohmann TOF:      {calendar.FormatDuration(HohmannTimeOfFlight)}");
        builder.AppendLine($"Synodic period:   {calendar.FormatDuration(SynodicPeriod)}");
        builder.AppendLine();
        builder.AppendLine("Best transfer:");
        builder.AppendLine(BestTransfer.ToSummaryText(calendar));
        return builder.ToString().TrimEnd();
    }
}

/// <summary>
/// Result of a scenario execution: computed transfer, launch analysis, and written files.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>Human-readable summary of the calculation.</summary>
    public required string ConsoleSummary { get; init; }
    /// <summary>Calculation mode ("single" or "porkchop").</summary>
    public required string Mode { get; init; }
    /// <summary>Mission calendar used for date formatting.</summary>
    public required MissionCalendar Calendar { get; init; }
    /// <summary>Computed transfer details (null for porkchop if no valid transfer).</summary>
    public TransferDetails? Transfer { get; init; }
    /// <summary>Porkchop scan result (null for single mode).</summary>
    public PorkchopResult? Porkchop { get; init; }
    /// <summary>Launch ascent analysis (null if not requested).</summary>
    public LaunchAnalysis? Launch { get; init; }
    /// <summary>Files written to disk during execution.</summary>
    public List<string> WrittenFiles { get; } = [];
}

/// <summary>
/// Launch ascent analysis: delta-V, losses, timing, and surface launch window.
/// </summary>
public sealed class LaunchAnalysis
{
    public required string OriginName { get; init; }
    public required string Mode { get; init; }
    public required double TargetPeriapsisAltitude { get; init; }
    public required double TargetApoapsisAltitude { get; init; }
    public required double TargetInclinationDeg { get; init; }
    public required double LaunchLatitudeDeg { get; init; }
    public required double LaunchLongitudeDeg { get; init; }
    public required double IdealDeltaVMps { get; init; }
    public required double RequiredDeltaVMps { get; init; }
    public required double GravityLossesMps { get; init; }
    public required double DragLossesMps { get; init; }
    public required double FinalAltitudeMeters { get; init; }
    public required double FinalVelocityMps { get; init; }
    public required double TimeToOrbitSeconds { get; init; }
    public required bool Successful { get; init; }
    public required bool IsEstimate { get; init; }
    public SurfaceLaunchTiming? SurfaceLaunchTiming { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Surface launch window timing details.
/// </summary>
public sealed class SurfaceLaunchTiming
{
    public required double LaunchTime { get; init; }
    public required double AscentLeadTimeSeconds { get; init; }
    public required double ParkingCoastTimeSeconds { get; init; }
    public required double ParkingOrbitPeriodSeconds { get; init; }
    public required int WholeParkingOrbits { get; init; }
    public required double ResidualParkingCoastSeconds { get; init; }
    public required double PlaneInclinationDeg { get; init; }
    public required double PlaneRaanDeg { get; init; }
    public required double LaunchAzimuthDeg { get; init; }
    public required double InertialLongitudeAtLaunchDeg { get; init; }
    public required bool UsesAscendingNode { get; init; }
    public required double EjectionDeclinationDeg { get; init; }
}
