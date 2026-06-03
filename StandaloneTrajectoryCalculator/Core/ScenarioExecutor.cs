using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StandaloneTrajectoryCalculator;

public static class ScenarioExecutor
{
    public static ExecutionResult Execute(ScenarioInput input, string inputPath, JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        var calendar = MissionCalendar.FromInput(input.Calendar);
        var centralBody = new CentralBody(input.CentralBody.Name, input.CentralBody.GravitationalParameter);
        var bodies = BuildBodies(input, centralBody);

        var request = input.Request ?? throw new InvalidOperationException("Request section is required.");
        if (string.IsNullOrWhiteSpace(request.Origin) || string.IsNullOrWhiteSpace(request.Destination))
        {
            throw new InvalidOperationException("Origin and destination names are required.");
        }

        if (request.Origin.Equals(request.Destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Origin and destination must be different bodies.");
        }

        if (!bodies.TryGetValue(request.Origin, out var origin))
        {
            throw new InvalidOperationException($"Unknown origin body '{request.Origin}'.");
        }

        if (!bodies.TryGetValue(request.Destination, out var destination))
        {
            throw new InvalidOperationException($"Unknown destination body '{request.Destination}'.");
        }

        var mode = request.Mode?.Trim().ToLowerInvariant() ?? "single";

        return mode switch
        {
            "single" => ExecuteSingle(request, inputPath, jsonOptions, calendar, centralBody, origin, destination),
            "porkchop" => ExecutePorkchop(request, inputPath, jsonOptions, calendar, centralBody, origin, destination),
            _ => throw new InvalidOperationException("Request mode must be 'single' or 'porkchop'.")
        };
    }

    private static ExecutionResult ExecuteSingle(
        CalculationRequest request,
        string inputPath,
        JsonSerializerOptions jsonOptions,
        MissionCalendar calendar,
        CentralBody centralBody,
        OrbitalBody origin,
        OrbitalBody destination)
    {
        if (!request.DepartureTime.HasValue || !request.TravelTime.HasValue)
        {
            throw new InvalidOperationException("Single mode requires departureTime and travelTime.");
        }

        var transfer = TransferCalculator.CalculateTransfer(
            origin,
            destination,
            centralBody,
            request.DepartureTime.Value,
            request.TravelTime.Value,
            request.ResolveDepartureOrbitPeriapsisAltitude(),
            request.ResolveDepartureOrbitApoapsisAltitude(),
            request.ArrivalParkingOrbitAltitude,
            request.ArrivalManeuverMode,
            request.UseAerobraking,
            request.LongWay);

        if (transfer is null)
        {
            throw new InvalidOperationException("The requested transfer is not physically achievable with the given parking-orbit constraints.");
        }

        var consoleSummary = transfer.ToSummaryText(calendar);
        var launch = BuildLaunchAnalysis(origin, request, transfer, calendar);

        if (launch is not null)
        {
            var combinedTotalDeltaV = transfer.DVTotal + launch.RequiredDeltaVMps;
            consoleSummary = consoleSummary
                + "\n"
                + $"Mission total dV:  {combinedTotalDeltaV:0.0} m/s"
                + "\n"
                + launch.Summary;
        }

        var result = new ExecutionResult
        {
            ConsoleSummary = consoleSummary,
            Mode = "single",
            Calendar = calendar,
            Transfer = transfer,
            Launch = launch
        };

        if (!string.IsNullOrWhiteSpace(request.ResultOutputPath))
        {
            var outputPath = ResolveOutputPath(inputPath, request.ResultOutputPath);
            WriteJson(outputPath, new
            {
                mode = "single",
                calendar,
                centralBody,
                transfer,
                launch
            }, jsonOptions);
            result.WrittenFiles.Add(outputPath);
        }

        return result;
    }

    private static ExecutionResult ExecutePorkchop(
        CalculationRequest request,
        string inputPath,
        JsonSerializerOptions jsonOptions,
        MissionCalendar calendar,
        CentralBody centralBody,
        OrbitalBody origin,
        OrbitalBody destination)
    {
        var porkchop = PorkchopCalculator.Calculate(origin, destination, centralBody, request);
        var consoleSummary = porkchop.ToSummaryText(calendar);
        var launch = BuildLaunchAnalysis(origin, request, porkchop.BestTransfer, calendar);

        if (launch is not null)
        {
            if (porkchop.BestTransfer is not null)
            {
                var combinedTotalDeltaV = porkchop.BestTransfer.DVTotal + launch.RequiredDeltaVMps;
                consoleSummary = consoleSummary
                    + "\n"
                    + $"Mission total dV:  {combinedTotalDeltaV:0.0} m/s"
                    + "\n"
                    + launch.Summary;
            }
            else
            {
                consoleSummary = consoleSummary + "\n" + launch.Summary;
            }
        }

        var result = new ExecutionResult
        {
            ConsoleSummary = consoleSummary,
            Mode = "porkchop",
            Calendar = calendar,
            Transfer = porkchop.BestTransfer,
            Porkchop = porkchop,
            Launch = launch
        };

        if (!string.IsNullOrWhiteSpace(request.CsvOutputPath))
        {
            var csvPath = ResolveOutputPath(inputPath, request.CsvOutputPath);
            WritePorkchopCsv(csvPath, porkchop, calendar);
            result.WrittenFiles.Add(csvPath);
        }

        if (!string.IsNullOrWhiteSpace(request.ResultOutputPath))
        {
            var outputPath = ResolveOutputPath(inputPath, request.ResultOutputPath);
            WriteJson(outputPath, new
            {
                mode = "porkchop",
                calendar,
                centralBody,
                porkchop.Window,
                porkchop.ValidPoints,
                porkchop.InvalidPoints,
                porkchop.HohmannTimeOfFlight,
                porkchop.SynodicPeriod,
                porkchop.BestTransfer,
                launch
            }, jsonOptions);
            result.WrittenFiles.Add(outputPath);
        }

        return result;
    }

    private static Dictionary<string, OrbitalBody> BuildBodies(ScenarioInput input, CentralBody centralBody)
    {
        if (input.Bodies.Count == 0)
        {
            throw new InvalidOperationException("At least one orbiting body must be defined.");
        }

        var bodies = new Dictionary<string, OrbitalBody>(StringComparer.OrdinalIgnoreCase);
        foreach (var body in input.Bodies)
        {
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                throw new InvalidOperationException("Every body needs a non-empty name.");
            }

            if (bodies.ContainsKey(body.Name))
            {
                throw new InvalidOperationException($"Duplicate body name '{body.Name}'.");
            }

            var argumentOfPeriapsisDeg = ResolveArgumentOfPeriapsisDegrees(body.Orbit);
            var meanAnomalyAtEpochDeg = ResolveMeanAnomalyAtEpochDegrees(body.Orbit);
            var secularTerms = BuildSecularTerms(body.Orbit);

            var orbit = new OrbitalElements(
                body.Orbit.SemiMajorAxis,
                body.Orbit.Eccentricity,
                body.Orbit.InclinationDeg * LambertSolver.Deg2Rad,
                body.Orbit.LongitudeOfAscendingNodeDeg * LambertSolver.Deg2Rad,
                argumentOfPeriapsisDeg * LambertSolver.Deg2Rad,
                meanAnomalyAtEpochDeg * LambertSolver.Deg2Rad,
                body.Orbit.Epoch,
                centralBody.GravitationalParameter,
                secularTerms);

            bodies.Add(
                body.Name,
                new OrbitalBody(
                    body.Name,
                    body.GravitationalParameter,
                    body.Radius,
                    body.SphereOfInfluence,
                    orbit,
                    body.RotationPeriodSeconds));
        }

        return bodies;
    }

    private static double ResolveArgumentOfPeriapsisDegrees(OrbitInput orbit)
    {
        if (orbit.LongitudeOfPeriapsisDeg.HasValue)
        {
            return NormalizeDegrees(orbit.LongitudeOfPeriapsisDeg.Value - orbit.LongitudeOfAscendingNodeDeg);
        }

        return orbit.ArgumentOfPeriapsisDeg;
    }

    private static double ResolveMeanAnomalyAtEpochDegrees(OrbitInput orbit)
    {
        if (orbit.MeanLongitudeDeg.HasValue && orbit.LongitudeOfPeriapsisDeg.HasValue)
        {
            var correction = orbit.MeanAnomalyCosineTermDeg ?? 0.0;
            return NormalizeDegrees(orbit.MeanLongitudeDeg.Value - orbit.LongitudeOfPeriapsisDeg.Value + correction);
        }

        return orbit.MeanAnomalyAtEpochDeg;
    }

    private static SecularOrbitTerms? BuildSecularTerms(OrbitInput orbit)
    {
        if (!orbit.MeanLongitudeDeg.HasValue && !orbit.LongitudeOfPeriapsisDeg.HasValue)
        {
            return null;
        }

        return new SecularOrbitTerms(
            orbit.SemiMajorAxisRate ?? 0.0,
            orbit.EccentricityRate ?? 0.0,
            (orbit.InclinationRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.LongitudeOfAscendingNodeRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            orbit.LongitudeOfPeriapsisDeg.GetValueOrDefault() * LambertSolver.Deg2Rad,
            (orbit.LongitudeOfPeriapsisRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            orbit.MeanLongitudeDeg.GetValueOrDefault() * LambertSolver.Deg2Rad,
            (orbit.MeanLongitudeRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalyQuadraticTermDegPerCentury2 ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalyCosineTermDeg ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalySineTermDeg ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalyFrequencyDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad);
    }

    private static double NormalizeDegrees(double angle)
    {
        angle %= 360.0;
        if (angle < 0)
        {
            angle += 360.0;
        }

        return angle;
    }

    private static void WriteJson(string outputPath, object data, JsonSerializerOptions jsonOptions)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(data, jsonOptions) + Environment.NewLine);
    }

    private static void WritePorkchopCsv(string outputPath, PorkchopResult result, MissionCalendar calendar)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("departureTime,travelTime,totalDeltaV,longWay,departureDate,travelDuration");

        foreach (var point in result.Points)
        {
            builder.Append(point.DepartureTime.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(point.TravelTime.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(point.TotalDeltaV?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty);
            builder.Append(',');
            builder.Append(point.LongWay?.ToString() ?? string.Empty);
            builder.Append(",\"");
            builder.Append(calendar.FormatDate(point.DepartureTime));
            builder.Append("\",\"");
            builder.Append(calendar.FormatDuration(point.TravelTime));
            builder.AppendLine("\"");
        }

        File.WriteAllText(outputPath, builder.ToString());
    }

    private static string ResolveOutputPath(string inputPath, string outputPath)
    {
        if (Path.IsPathRooted(outputPath))
        {
            return outputPath;
        }

        var baseDirectory = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDirectory, outputPath));
    }

    private static LaunchAnalysis? BuildLaunchAnalysis(
        OrbitalBody originBody,
        CalculationRequest request,
        TransferDetails? transfer,
        MissionCalendar calendar)
    {
        if (!(request.Launch?.Enabled ?? false))
        {
            return null;
        }

        var launchConfig = request.Launch!;
        var targetPeriapsisAltitude = request.ResolveDepartureOrbitPeriapsisAltitude();
        var targetApoapsisAltitude = request.ResolveDepartureOrbitApoapsisAltitude();

        try
        {
            var planet = OrbitalAscentCalculator.CreatePlanetFromBody(originBody);
            var targetOrbit = new OrbitalAscentCalculator.TargetOrbit
            {
                PeriapsisAltitude = targetPeriapsisAltitude,
                ApoapsisAltitude = targetApoapsisAltitude,
                Inclination = launchConfig.TargetInclination
            };

            var rocket = new OrbitalAscentCalculator.RocketParams
            {
                InitialMass = launchConfig.RocketInitialMass,
                Thrust = launchConfig.RocketThrust,
                Isp = launchConfig.RocketIsp
            };

            var result = launchConfig.Mode?.ToLowerInvariant() == "quick"
                ? OrbitalAscentCalculator.EstimateAscentQuick(planet, targetOrbit, launchConfig.LaunchLatitude)
                : OrbitalAscentCalculator.SimulateAscent(planet, targetOrbit, rocket, launchConfig.LaunchLatitude);

            var ascentLeadTimeSeconds = EstimateAscentLeadTimeSeconds(planet, rocket, result);
            var surfaceLaunchTiming = transfer is null
                ? null
                : SurfaceLaunchTimingCalculator.Analyze(
                    originBody,
                    transfer,
                    launchConfig,
                    targetPeriapsisAltitude,
                    targetApoapsisAltitude,
                    ascentLeadTimeSeconds);

            return new LaunchAnalysis
            {
                OriginName = originBody.Name,
                Mode = launchConfig.Mode?.ToLowerInvariant() == "quick" ? "quick" : "detailed",
                TargetPeriapsisAltitude = targetPeriapsisAltitude,
                TargetApoapsisAltitude = targetApoapsisAltitude,
                TargetInclinationDeg = launchConfig.TargetInclination,
                LaunchLatitudeDeg = launchConfig.LaunchLatitude,
                LaunchLongitudeDeg = launchConfig.LaunchLongitude,
                IdealDeltaVMps = result.IdealDeltaV,
                RequiredDeltaVMps = result.ExpendedDeltaV,
                GravityLossesMps = result.GravityLosses,
                DragLossesMps = result.DragLosses,
                FinalAltitudeMeters = result.FinalAltitude,
                FinalVelocityMps = result.FinalVelocity,
                TimeToOrbitSeconds = ascentLeadTimeSeconds,
                Successful = result.Successful,
                IsEstimate = result.IsEstimate,
                SurfaceLaunchTiming = surfaceLaunchTiming?.Timing,
                Summary = BuildLaunchSummary(
                    OrbitalAscentCalculator.GetResultSummary(planet, targetOrbit, result),
                    surfaceLaunchTiming,
                    calendar,
                    transfer)
            };
        }
        catch (Exception ex)
        {
            return new LaunchAnalysis
            {
                OriginName = originBody.Name,
                Mode = launchConfig.Mode?.ToLowerInvariant() == "quick" ? "quick" : "detailed",
                TargetPeriapsisAltitude = targetPeriapsisAltitude,
                TargetApoapsisAltitude = targetApoapsisAltitude,
                TargetInclinationDeg = launchConfig.TargetInclination,
                LaunchLatitudeDeg = launchConfig.LaunchLatitude,
                LaunchLongitudeDeg = launchConfig.LaunchLongitude,
                IdealDeltaVMps = 0.0,
                RequiredDeltaVMps = 0.0,
                GravityLossesMps = 0.0,
                DragLossesMps = 0.0,
                FinalAltitudeMeters = 0.0,
                FinalVelocityMps = 0.0,
                TimeToOrbitSeconds = 0.0,
                Successful = false,
                IsEstimate = true,
                SurfaceLaunchTiming = null,
                Summary = $"Error while calculating launch ascent: {ex.Message}"
            };
        }
    }

    private static double EstimateAscentLeadTimeSeconds(
        OrbitalAscentCalculator.PlanetParams planet,
        OrbitalAscentCalculator.RocketParams rocket,
        OrbitalAscentCalculator.AscentResult result)
    {
        if (result.TimeToOrbit > 0.0)
        {
            return result.TimeToOrbit;
        }

        if (rocket.InitialMass <= 0.0 || rocket.Thrust <= 0.0)
        {
            return 0.0;
        }

        var surfaceGravity = planet.Mu / (planet.Radius * planet.Radius);
        var netAcceleration = Math.Max(0.05, (rocket.Thrust / (rocket.InitialMass * 0.65)) - (0.72 * surfaceGravity));
        return result.ExpendedDeltaV / netAcceleration;
    }

    private static string BuildLaunchSummary(
        string ascentSummary,
        SurfaceLaunchTimingCalculator.Outcome? timingOutcome,
        MissionCalendar calendar,
        TransferDetails? transfer)
    {
        var timing = timingOutcome?.Timing;
        if (timing is null)
        {
            var reason = string.IsNullOrWhiteSpace(timingOutcome?.FailureReason)
                ? "Surface launch timing is unavailable."
                : timingOutcome!.FailureReason;

            if (timingOutcome?.SuggestedTiming is not null && timingOutcome.SuggestedInclinationDeg.HasValue)
            {
                return string.Join(
                    Environment.NewLine,
                    ascentSummary,
                    "Surface launch window: unavailable",
                    reason,
                    $"Try target inclination: {timingOutcome.SuggestedInclinationDeg.Value:0.00} deg (prograde {timingOutcome.SuggestedProgradeInclinationDeg.GetValueOrDefault():0.00} / retrograde {timingOutcome.SuggestedRetrogradeInclinationDeg.GetValueOrDefault():0.00})",
                    "Fallback window with suggested inclination:",
                    FormatTimingDetails(timingOutcome.SuggestedTiming, calendar, transfer));
            }

            return ascentSummary + Environment.NewLine + "Surface launch window: unavailable" + Environment.NewLine + reason;
        }

        return string.Join(
            Environment.NewLine,
            ascentSummary,
            "Surface launch window:",
            FormatTimingDetails(timing, calendar, transfer));
    }

    private static string FormatTimingDetails(
        SurfaceLaunchTiming timing,
        MissionCalendar calendar,
        TransferDetails? transfer)
    {
        var liftoffToDeparture = transfer is null
            ? timing.AscentLeadTimeSeconds + timing.ParkingCoastTimeSeconds
            : Math.Max(0.0, transfer.DepartureTime - timing.LaunchTime);
        var nodeLabel = timing.UsesAscendingNode ? "ascending" : "descending";

        return string.Join(
            Environment.NewLine,
            $"Launch time:      {calendar.FormatDate(timing.LaunchTime)}",
            $"Liftoff to burn:  {calendar.FormatDuration(liftoffToDeparture)}",
            $"Ascent lead:      {calendar.FormatDuration(timing.AscentLeadTimeSeconds)}",
            $"Parking coast:    {calendar.FormatDuration(timing.ParkingCoastTimeSeconds)}",
            $"Coast breakdown:  {timing.WholeParkingOrbits} orbit(s) + {calendar.FormatDuration(timing.ResidualParkingCoastSeconds)}",
            $"Launch azimuth:   {timing.LaunchAzimuthDeg:0.00} deg",
            $"Plane node:       {nodeLabel}",
            $"Plane incl.:      {timing.PlaneInclinationDeg:0.00} deg",
            $"Plane RAAN:       {timing.PlaneRaanDeg:0.00} deg",
            $"Launch longitude: {timing.InertialLongitudeAtLaunchDeg:0.00} deg inertial",
            $"Ejection decl.:   {timing.EjectionDeclinationDeg:0.00} deg");
    }
}
