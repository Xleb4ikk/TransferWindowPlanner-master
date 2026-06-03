namespace StandaloneTrajectoryCalculator;

public static class TransferCalculator
{
    public static TransferDetails? CalculateTransfer(
        OrbitalBody origin,
        OrbitalBody destination,
        CentralBody centralBody,
        double departureTime,
        double travelTime,
        double departureParkingOrbitPeriapsisAltitude,
        double? departureParkingOrbitApoapsisAltitude,
        double? arrivalParkingOrbitAltitude,
        string? arrivalManeuverMode,
        bool useAerobraking,
        bool? longWayOverride)
    {
        if (travelTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(travelTime), "Travel time must be positive.");
        }

        if (departureParkingOrbitPeriapsisAltitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departureParkingOrbitPeriapsisAltitude), "Departure orbit periapsis altitude cannot be negative.");
        }

        if (departureParkingOrbitApoapsisAltitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departureParkingOrbitApoapsisAltitude), "Departure orbit apoapsis altitude cannot be negative.");
        }

        if (departureParkingOrbitApoapsisAltitude.HasValue &&
            departureParkingOrbitApoapsisAltitude.Value < departureParkingOrbitPeriapsisAltitude)
        {
            throw new ArgumentOutOfRangeException(nameof(departureParkingOrbitApoapsisAltitude), "Departure orbit apoapsis altitude cannot be lower than periapsis altitude.");
        }

        if (arrivalParkingOrbitAltitude < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrivalParkingOrbitAltitude), "Arrival orbit altitude cannot be negative.");
        }

        var originPosition = origin.Orbit.PositionAtTime(departureTime);
        var originVelocity = origin.Orbit.VelocityAtTime(departureTime);
        var destinationPosition = destination.Orbit.PositionAtTime(departureTime + travelTime);
        var shortTransferAngle = Vector3D.AngleRadians(originPosition, destinationPosition);

        // Determine the signed angle in range [0, 2π). Use this to decide
        // whether the long-way Lambert branch (angle > π) should be used.
        var signedAngle = Math.Atan2(Vector3D.Cross(originPosition, destinationPosition).Z, Vector3D.Dot(originPosition, destinationPosition));
        if (signedAngle < 0)
        {
            signedAngle += LambertSolver.TwoPi;
        }

        var longWay = longWayOverride ?? (signedAngle > Math.PI);

        Vector3D transferFinalVelocity;
        Vector3D transferInitialVelocity;

        try
        {
            transferInitialVelocity = LambertSolver.Solve(
                centralBody.GravitationalParameter,
                originPosition,
                destinationPosition,
                travelTime,
                longWay,
                out transferFinalVelocity);
        }
        catch
        {
            return null;
        }

        var ejectionDeltaVector = transferInitialVelocity - originVelocity;
        var ejectionDeltaV = ejectionDeltaVector.Magnitude;
        var ejectionInclination = 0.0;
        var vesselOriginOrbitalSpeed = 0.0;

        var departureParkingOrbitApoapsisResolved = departureParkingOrbitApoapsisAltitude ?? departureParkingOrbitPeriapsisAltitude;

        if (departureParkingOrbitPeriapsisAltitude > 0)
        {
            if (!origin.SphereOfInfluence.HasValue)
            {
                throw new InvalidOperationException($"Body '{origin.Name}' needs sphereOfInfluence when departure parking orbit is used.");
            }

            var mu = origin.GravitationalParameter;
            var periapsisRadius = departureParkingOrbitPeriapsisAltitude + origin.Radius;
            var apoapsisRadius = departureParkingOrbitApoapsisResolved + origin.Radius;
            var semiMajorAxis = (periapsisRadius + apoapsisRadius) * 0.5;
            var sphereOfInfluence = origin.SphereOfInfluence.Value;
            var parkingOrbitSpeed = Math.Sqrt(mu * (2.0 / periapsisRadius - 1.0 / semiMajorAxis));
            var velocityAtPeriapsis = Math.Sqrt(
                ejectionDeltaV * ejectionDeltaV +
                2 * mu / periapsisRadius -
                2 * mu / sphereOfInfluence);

            vesselOriginOrbitalSpeed = parkingOrbitSpeed;

            var eccentricity = periapsisRadius * velocityAtPeriapsis * velocityAtPeriapsis / mu - 1;
            var apoapsis = periapsisRadius * (1 + eccentricity) / (1 - eccentricity);
            if (apoapsis > 0 && apoapsis <= sphereOfInfluence)
            {
                return null;
            }

            if (ejectionDeltaVector.Z != 0 && ejectionDeltaV > 0)
            {
                var sinInclination = ejectionDeltaVector.Z / ejectionDeltaV;
                ejectionInclination = Math.Asin(Math.Clamp(sinInclination, -1.0, 1.0));
                ejectionDeltaV = Math.Sqrt(
                    parkingOrbitSpeed * parkingOrbitSpeed +
                    velocityAtPeriapsis * velocityAtPeriapsis -
                    2 * parkingOrbitSpeed * velocityAtPeriapsis * Math.Sqrt(1 - sinInclination * sinInclination));
            }
            else
            {
                ejectionDeltaV = velocityAtPeriapsis - parkingOrbitSpeed;
            }
        }

        var transfer = new TransferDetails
        {
            OriginName = origin.Name,
            DestinationName = destination.Name,
            DepartureTime = departureTime,
            TravelTime = travelTime,
            LongWay = longWay,
            OriginPositionAtDeparture = originPosition,
            DestinationPositionAtArrival = destinationPosition,
            OriginVelocity = originVelocity,
            TransferInitialVelocity = transferInitialVelocity,
            TransferFinalVelocity = transferFinalVelocity,
            OriginVesselOrbitalSpeed = vesselOriginOrbitalSpeed,
            OriginBodyRadius = origin.Radius,
            OriginOrbitPeriapsisRadius = departureParkingOrbitPeriapsisAltitude > 0 ? departureParkingOrbitPeriapsisAltitude + origin.Radius : 0.0,
            OriginOrbitApoapsisRadius = departureParkingOrbitPeriapsisAltitude > 0 ? departureParkingOrbitApoapsisResolved + origin.Radius : 0.0,
            TransferAngle = longWay ? LambertSolver.TwoPi - shortTransferAngle : shortTransferAngle,
            EjectionDeltaVector = ejectionDeltaVector.Magnitude > 0 ? ejectionDeltaVector.Normalized * ejectionDeltaV : Vector3D.Zero,
            EjectionInclination = ejectionInclination,
            PhaseAngle = PhaseAngleAtDeparture(origin, destination, departureTime)
        };

        if (arrivalParkingOrbitAltitude.HasValue)
        {
            if (useAerobraking && !SupportsAerobraking(destination))
            {
                throw new InvalidOperationException($"Body '{destination.Name}' does not support built-in aerobraking mode.");
            }

            var destinationVelocity = destination.Orbit.VelocityAtTime(departureTime + travelTime);
            var insertionDeltaVector = transferFinalVelocity - destinationVelocity;
            var insertionDeltaV = insertionDeltaVector.Magnitude;
            var insertionInclination = 0.0;
            var normalizedArrivalManeuverMode = NormalizeArrivalManeuverMode(arrivalManeuverMode, arrivalParkingOrbitAltitude, useAerobraking);

            if (insertionDeltaVector.Z != 0 && insertionDeltaV > 0)
            {
                insertionInclination = Math.Asin(Math.Clamp(insertionDeltaVector.Z / insertionDeltaV, -1.0, 1.0));
            }

            transfer.DestinationVelocity = destinationVelocity;
            transfer.InsertionInclination = insertionInclination;
            transfer.ArrivalManeuverMode = normalizedArrivalManeuverMode;

            if (arrivalParkingOrbitAltitude.Value != 0)
            {
                if (!destination.SphereOfInfluence.HasValue)
                {
                    throw new InvalidOperationException($"Body '{destination.Name}' needs sphereOfInfluence when arrival parking orbit is used.");
                }

                var mu = destination.GravitationalParameter;
                var periapsisRadius = arrivalParkingOrbitAltitude.Value + destination.Radius;
                var sphereOfInfluence = destination.SphereOfInfluence.Value;
                var hyperbolicPeriapsisSpeed = Math.Sqrt(
                    insertionDeltaV * insertionDeltaV +
                    2 * mu / periapsisRadius -
                    2 * mu / sphereOfInfluence);
                double targetPeriapsisSpeed;

                if (normalizedArrivalManeuverMode == "circular-capture")
                {
                    targetPeriapsisSpeed = Math.Sqrt(mu / periapsisRadius);
                    transfer.ArrivalCapturePeriapsisRadius = periapsisRadius;
                    transfer.ArrivalCaptureApoapsisRadius = periapsisRadius;
                }
                else if (normalizedArrivalManeuverMode == "aerobraking")
                {
                    targetPeriapsisSpeed = hyperbolicPeriapsisSpeed;
                    transfer.ArrivalCapturePeriapsisRadius = periapsisRadius;
                    transfer.ArrivalCaptureApoapsisRadius = null;
                }
                else
                {
                    // Capture into a high ellipse: the user-provided arrival altitude is the
                    // periapsis of the first bound orbit after the braking burn.
                    var captureApoapsisRadius = Math.Max(periapsisRadius + 1.0, sphereOfInfluence);
                    targetPeriapsisSpeed = Math.Sqrt(mu * (2.0 / periapsisRadius - 2.0 / (periapsisRadius + captureApoapsisRadius)));
                    transfer.ArrivalCapturePeriapsisRadius = periapsisRadius;
                    transfer.ArrivalCaptureApoapsisRadius = captureApoapsisRadius;
                }

                transfer.DestinationVesselOrbitalSpeed = targetPeriapsisSpeed;
                insertionDeltaV = normalizedArrivalManeuverMode == "aerobraking"
                    ? 0.0
                    : Math.Max(0.0, hyperbolicPeriapsisSpeed - targetPeriapsisSpeed);

                transfer.InjectionDeltaVector = insertionDeltaVector.Magnitude > 0
                    ? insertionDeltaVector.Normalized * insertionDeltaV
                    : Vector3D.Zero;
            }
            else
            {
                transfer.DestinationVesselOrbitalSpeed = transferFinalVelocity.Magnitude;
                transfer.ArrivalCapturePeriapsisRadius = null;
                transfer.ArrivalCaptureApoapsisRadius = null;
                transfer.InjectionDeltaVector = Vector3D.Zero;
            }
        }

        if (departureParkingOrbitPeriapsisAltitude > 0)
        {
            try
            {
                transfer.CalculateEjectionValues(origin, destination);
            }
            catch
            {
                // Transfer itself remains valid even if the ejection-angle geometry is singular.
            }
        }

        return transfer;
    }

    public static double HohmannTimeOfFlight(OrbitalBody origin, OrbitalBody destination)
    {
        var semiMajorAxis = (origin.Orbit.SemiMajorAxis + destination.Orbit.SemiMajorAxis) * 0.5;
        return Math.PI * Math.Sqrt(Math.Pow(semiMajorAxis, 3) / origin.Orbit.ParentGravitationalParameter);
    }

    public static double SynodicPeriod(OrbitalBody origin, OrbitalBody destination)
    {
        return Math.Abs(1.0 / (1.0 / origin.Orbit.Period - 1.0 / destination.Orbit.Period));
    }

    private static double PhaseAngleAtDeparture(OrbitalBody origin, OrbitalBody destination, double departureTime)
    {
        var originPosition = origin.Orbit.PositionAtTime(departureTime);
        var destinationPosition = destination.Orbit.PositionAtTime(departureTime);
        var angle = Math.Atan2(Vector3D.Cross(originPosition, destinationPosition).Z, Vector3D.Dot(originPosition, destinationPosition));
        if (angle < 0)
        {
            angle += LambertSolver.TwoPi;
        }

        if (destination.Orbit.SemiMajorAxis < origin.Orbit.SemiMajorAxis)
        {
            angle -= LambertSolver.TwoPi;
        }

        return angle;
    }

    private static string NormalizeArrivalManeuverMode(string? arrivalManeuverMode, double? arrivalParkingOrbitAltitude, bool useAerobraking)
    {
        if (!arrivalParkingOrbitAltitude.HasValue)
        {
            return "ignore-arrival-burn";
        }

        if (arrivalParkingOrbitAltitude.Value == 0)
        {
            return "flyby";
        }

        if (useAerobraking)
        {
            return "aerobraking";
        }

        return arrivalManeuverMode?.Trim().ToLowerInvariant() switch
        {
            "circular-capture" => "circular-capture",
            "capture orbit" => "circular-capture",
            "elliptic-capture" => "elliptic-capture",
            "high-elliptic-capture" => "elliptic-capture",
            "elliptic capture" => "elliptic-capture",
            _ => "circular-capture"
        };
    }

    private static bool SupportsAerobraking(OrbitalBody body)
    {
        return body.Name.Contains("Earth", StringComparison.OrdinalIgnoreCase) ||
               body.Name.Contains("Venus", StringComparison.OrdinalIgnoreCase);
    }
}
