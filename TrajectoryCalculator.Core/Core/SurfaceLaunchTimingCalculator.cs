namespace TrajectoryCalculator;

/// <summary>
/// Analyzes surface launch windows and timing for interplanetary transfers.
/// </summary>
public static class SurfaceLaunchTimingCalculator
{
    public sealed class Outcome
    {
        public SurfaceLaunchTiming? Timing { get; init; }
        public string? FailureReason { get; init; }
        public double? SuggestedProgradeInclinationDeg { get; init; }
        public double? SuggestedRetrogradeInclinationDeg { get; init; }
        public double? SuggestedInclinationDeg { get; init; }
        public SurfaceLaunchTiming? SuggestedTiming { get; init; }
    }

    public static Outcome Analyze(
        OrbitalBody originBody,
        TransferDetails transfer,
        LaunchConfiguration launchConfig,
        double targetPeriapsisAltitude,
        double targetApoapsisAltitude,
        double ascentLeadTimeSeconds)
    {
        var rotationPeriod = originBody.RotationPeriodSeconds;
        if (!rotationPeriod.HasValue || Math.Abs(rotationPeriod.Value) < 1e-9)
        {
            return new Outcome
            {
                FailureReason = "Surface launch timing needs origin body rotationPeriodSeconds."
            };
        }

        var ejectionVector = transfer.EjectionVector;
        if (ejectionVector.Magnitude <= 1e-9)
        {
            return new Outcome
            {
                FailureReason = "Surface launch timing needs a valid departure ejection vector."
            };
        }

        var latitudeRad = launchConfig.LaunchLatitude * LambertSolver.Deg2Rad;
        var siteLongitudeRad = NormalizeRadians(launchConfig.LaunchLongitude * LambertSolver.Deg2Rad);
        var ejectionDirection = ejectionVector.Normalized;
        var rotationRate = LambertSolver.TwoPi / rotationPeriod.Value;
        var rotationCycle = Math.Abs(rotationPeriod.Value);

        if (transfer.DepartureTime <= ascentLeadTimeSeconds)
        {
            return new Outcome
            {
                FailureReason = "Departure time must be later than the modeled ascent lead time."
            };
        }

        var requestedInclinationDeg = NormalizeInclinationDegrees(launchConfig.TargetInclination);
        var requestedInclinationRad = requestedInclinationDeg * LambertSolver.Deg2Rad;
        var planeNormals = ResolvePlaneNormals(ejectionDirection, requestedInclinationRad);
        if (planeNormals.Count == 0)
        {
            var ejectionDeclination = Math.Asin(Math.Clamp(ejectionDirection.Z, -1.0, 1.0)) * LambertSolver.Rad2Deg;
            var minimumCompatibleInclination = RoundUpDegrees(
                Math.Min(90.0, Math.Max(Math.Abs(launchConfig.LaunchLatitude), Math.Abs(ejectionDeclination))),
                0.1);
            var retrogradeMirrorInclination = 180.0 - minimumCompatibleInclination;
            var suggestedInclination = Math.Abs(requestedInclinationDeg - minimumCompatibleInclination) <= Math.Abs(requestedInclinationDeg - retrogradeMirrorInclination)
                ? minimumCompatibleInclination
                : retrogradeMirrorInclination;
            var suggestedTiming = AnalyzeCore(
                originBody,
                transfer,
                targetPeriapsisAltitude,
                targetApoapsisAltitude,
                ascentLeadTimeSeconds,
                minimumCompatibleInclination,
                latitudeRad,
                siteLongitudeRad,
                ejectionDirection,
                rotationRate,
                rotationCycle);

            return new Outcome
            {
                FailureReason =
                    $"Requested inclination {requestedInclinationDeg:0.00} deg cannot contain the departure vector (declination {ejectionDeclination:0.00} deg).",
                SuggestedProgradeInclinationDeg = minimumCompatibleInclination,
                SuggestedRetrogradeInclinationDeg = retrogradeMirrorInclination,
                SuggestedInclinationDeg = suggestedInclination,
                SuggestedTiming = suggestedTiming
            };
        }

        var timing = AnalyzeCore(
            originBody,
            transfer,
            targetPeriapsisAltitude,
            targetApoapsisAltitude,
            ascentLeadTimeSeconds,
            requestedInclinationDeg,
            latitudeRad,
            siteLongitudeRad,
            ejectionDirection,
            rotationRate,
            rotationCycle);

        if (timing is null)
        {
            return new Outcome
            {
                FailureReason =
                    $"Launch latitude {launchConfig.LaunchLatitude:0.00} deg cannot reach inclination {requestedInclinationDeg:0.00} deg in the computed departure plane."
            };
        }

        return new Outcome
        {
            Timing = timing
        };
    }

    private static SurfaceLaunchTiming? AnalyzeCore(
        OrbitalBody originBody,
        TransferDetails transfer,
        double targetPeriapsisAltitude,
        double targetApoapsisAltitude,
        double ascentLeadTimeSeconds,
        double inclinationDeg,
        double latitudeRad,
        double siteLongitudeRad,
        Vector3D ejectionDirection,
        double rotationRate,
        double rotationCycle)
    {
        var planeNormals = ResolvePlaneNormals(ejectionDirection, inclinationDeg * LambertSolver.Deg2Rad);
        if (planeNormals.Count == 0)
        {
            return null;
        }

        var availableLaunchTime = transfer.DepartureTime - ascentLeadTimeSeconds;
        var launchWindows = new List<CandidateWindow>();

        foreach (var planeNormal in planeNormals)
        {
            foreach (var crossingLongitude in ResolvePlaneCrossings(planeNormal, latitudeRad))
            {
                var launchTime = SolveLatestCrossingTime(crossingLongitude, siteLongitudeRad, rotationRate, rotationCycle, availableLaunchTime);
                var inertialLongitudeAtLaunch = NormalizeRadians(siteLongitudeRad + rotationRate * launchTime);
                var surfacePosition = SurfacePosition(latitudeRad, inertialLongitudeAtLaunch);
                var orbitalVelocity = Vector3D.Cross(planeNormal, surfacePosition).Normalized;
                if (orbitalVelocity.Magnitude <= 1e-9)
                {
                    continue;
                }

                var east = EastVector(inertialLongitudeAtLaunch);
                var north = Vector3D.Cross(surfacePosition, east).Normalized;
                var launchAzimuth = NormalizeDegrees(Math.Atan2(
                        Vector3D.Dot(orbitalVelocity, east),
                        Vector3D.Dot(orbitalVelocity, north)) * LambertSolver.Rad2Deg);
                var coastTime = Math.Max(0.0, availableLaunchTime - launchTime);

                launchWindows.Add(new CandidateWindow
                {
                    LaunchTime = launchTime,
                    CoastTimeSeconds = coastTime,
                    PlaneNormal = planeNormal,
                    LaunchAzimuthDeg = launchAzimuth,
                    InertialLongitudeDeg = inertialLongitudeAtLaunch * LambertSolver.Rad2Deg,
                    UsesAscendingNode = orbitalVelocity.Z >= 0.0
                });
            }
        }

        if (launchWindows.Count == 0)
        {
            return null;
        }

        var bestWindow = launchWindows
            .OrderByDescending(window => window.LaunchTime)
            .First();

        var periapsisRadius = originBody.Radius + targetPeriapsisAltitude;
        var apoapsisRadius = originBody.Radius + targetApoapsisAltitude;
        var semiMajorAxis = (periapsisRadius + apoapsisRadius) * 0.5;
        var parkingOrbitPeriod = LambertSolver.TwoPi * Math.Sqrt(Math.Pow(semiMajorAxis, 3) / originBody.GravitationalParameter);
        var wholeParkingOrbits = parkingOrbitPeriod > 0.0
            ? (int)Math.Floor(bestWindow.CoastTimeSeconds / parkingOrbitPeriod)
            : 0;
        var residualParkingCoast = parkingOrbitPeriod > 0.0
            ? bestWindow.CoastTimeSeconds - (wholeParkingOrbits * parkingOrbitPeriod)
            : bestWindow.CoastTimeSeconds;
        var planeRaanDeg = NormalizeDegrees(Math.Atan2(bestWindow.PlaneNormal.X, -bestWindow.PlaneNormal.Y) * LambertSolver.Rad2Deg);
        var ejectionDeclinationDeg = Math.Asin(Math.Clamp(ejectionDirection.Z, -1.0, 1.0)) * LambertSolver.Rad2Deg;

        return new SurfaceLaunchTiming
        {
            LaunchTime = bestWindow.LaunchTime,
            AscentLeadTimeSeconds = ascentLeadTimeSeconds,
            ParkingCoastTimeSeconds = bestWindow.CoastTimeSeconds,
            ParkingOrbitPeriodSeconds = parkingOrbitPeriod,
            WholeParkingOrbits = wholeParkingOrbits,
            ResidualParkingCoastSeconds = residualParkingCoast,
            PlaneInclinationDeg = inclinationDeg,
            PlaneRaanDeg = planeRaanDeg,
            LaunchAzimuthDeg = bestWindow.LaunchAzimuthDeg,
            InertialLongitudeAtLaunchDeg = NormalizeDegrees(bestWindow.InertialLongitudeDeg),
            UsesAscendingNode = bestWindow.UsesAscendingNode,
            EjectionDeclinationDeg = ejectionDeclinationDeg
        };
    }

    private static List<Vector3D> ResolvePlaneNormals(Vector3D ejectionDirection, double inclinationRad)
    {
        var result = new List<Vector3D>();
        var nz = Math.Cos(inclinationRad);
        var planarMagnitude = Math.Sin(inclinationRad);
        var horizontalMagnitude = Math.Sqrt((ejectionDirection.X * ejectionDirection.X) + (ejectionDirection.Y * ejectionDirection.Y));

        if (horizontalMagnitude < 1e-12)
        {
            return result;
        }

        var alongHorizontal = (-nz * ejectionDirection.Z) / horizontalMagnitude;
        var limit = planarMagnitude + 1e-12;
        if (Math.Abs(alongHorizontal) > limit)
        {
            return result;
        }

        var perpendicularMagnitude = Math.Sqrt(Math.Max(0.0, (planarMagnitude * planarMagnitude) - (alongHorizontal * alongHorizontal)));
        var horizontalUnit = new Vector3D(ejectionDirection.X / horizontalMagnitude, ejectionDirection.Y / horizontalMagnitude, 0.0);
        var perpendicularUnit = new Vector3D(-horizontalUnit.Y, horizontalUnit.X, 0.0);

        result.Add(NormalizeNormal((horizontalUnit * alongHorizontal) + (perpendicularUnit * perpendicularMagnitude), nz));

        if (perpendicularMagnitude > 1e-12)
        {
            result.Add(NormalizeNormal((horizontalUnit * alongHorizontal) - (perpendicularUnit * perpendicularMagnitude), nz));
        }

        return result;
    }

    private static IEnumerable<double> ResolvePlaneCrossings(Vector3D planeNormal, double latitudeRad)
    {
        var cosLatitude = Math.Cos(latitudeRad);
        var a = planeNormal.X * cosLatitude;
        var b = planeNormal.Y * cosLatitude;
        var c = planeNormal.Z * Math.Sin(latitudeRad);
        var radius = Math.Sqrt((a * a) + (b * b));

        if (radius < 1e-12)
        {
            yield break;
        }

        var cosineArgument = Math.Clamp(-c / radius, -1.0, 1.0);
        var phase = Math.Atan2(b, a);
        var offset = Math.Acos(cosineArgument);

        yield return NormalizeRadians(phase + offset);

        if (offset > 1e-12)
        {
            yield return NormalizeRadians(phase - offset);
        }
    }

    private static double SolveLatestCrossingTime(
        double targetInertialLongitude,
        double siteLongitudeAtEpoch,
        double rotationRate,
        double rotationCycle,
        double latestAllowedTime)
    {
        var baseTime = (targetInertialLongitude - siteLongitudeAtEpoch) / rotationRate;
        var cycles = Math.Floor((latestAllowedTime - baseTime) / rotationCycle);
        var time = baseTime + (cycles * rotationCycle);

        if (time > latestAllowedTime + 1e-9)
        {
            time -= rotationCycle;
        }

        return time;
    }

    private static Vector3D SurfacePosition(double latitudeRad, double inertialLongitudeRad)
    {
        var cosLatitude = Math.Cos(latitudeRad);
        return new Vector3D(
            cosLatitude * Math.Cos(inertialLongitudeRad),
            cosLatitude * Math.Sin(inertialLongitudeRad),
            Math.Sin(latitudeRad));
    }

    private static Vector3D EastVector(double inertialLongitudeRad)
    {
        return new Vector3D(-Math.Sin(inertialLongitudeRad), Math.Cos(inertialLongitudeRad), 0.0);
    }

    private static Vector3D NormalizeNormal(Vector3D horizontal, double z)
    {
        return new Vector3D(horizontal.X, horizontal.Y, z).Normalized;
    }

    private static double NormalizeInclinationDegrees(double inclinationDeg)
    {
        var normalized = inclinationDeg % 360.0;
        if (normalized < 0.0)
        {
            normalized += 360.0;
        }

        if (normalized > 180.0)
        {
            normalized = 360.0 - normalized;
        }

        return normalized;
    }

    private static double NormalizeRadians(double angle)
    {
        angle %= LambertSolver.TwoPi;
        if (angle < 0.0)
        {
            angle += LambertSolver.TwoPi;
        }

        return angle;
    }

    private static double NormalizeDegrees(double angle)
    {
        angle %= 360.0;
        if (angle < 0.0)
        {
            angle += 360.0;
        }

        return angle;
    }

    private static double RoundUpDegrees(double value, double step)
    {
        if (step <= 0.0)
        {
            return value;
        }

        return Math.Ceiling(value / step) * step;
    }

    private sealed class CandidateWindow
    {
        public required double LaunchTime { get; init; }
        public required double CoastTimeSeconds { get; init; }
        public required Vector3D PlaneNormal { get; init; }
        public required double LaunchAzimuthDeg { get; init; }
        public required double InertialLongitudeDeg { get; init; }
        public required bool UsesAscendingNode { get; init; }
    }
}
