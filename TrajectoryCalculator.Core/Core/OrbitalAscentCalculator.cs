using System;

namespace TrajectoryCalculator;

/// <summary>
/// Simulates or estimates orbital ascent from a planetary surface.
/// </summary>
public static class OrbitalAscentCalculator
{
    private const double StandardGravity = 9.80665;
    private const double DefaultDryMassFraction = 0.10;

    public struct PlanetParams
    {
        public string Name;
        public double Mu;
        public double Radius;
        public double RotationPeriod;
        public bool HasAtmosphere;

        public double EquatorialVelocity => Math.Abs(RotationPeriod) > 1e-9
            ? (2.0 * Math.PI * Radius) / RotationPeriod
            : 0.0;
    }

    public struct TargetOrbit
    {
        public double PeriapsisAltitude;
        public double ApoapsisAltitude;
        public double Inclination;
    }

    public struct RocketParams
    {
        public double InitialMass;
        public double Thrust;
        public double Isp;

        public double MassFlowRate => Thrust > 0.0 && Isp > 0.0
            ? Thrust / (Isp * StandardGravity)
            : 0.0;
    }

    public struct AscentResult
    {
        public double TimeToOrbit;
        public double FinalAltitude;
        public double FinalVelocity;
        public double IdealDeltaV;
        public double ExpendedDeltaV;
        public double GravityLosses;
        public double DragLosses;
        public bool Successful;
        public bool IsEstimate;
    }

    public static AscentResult SimulateAscent(
        PlanetParams planet,
        TargetOrbit targetOrbit,
        RocketParams rocket,
        double latitude = 0.0)
    {
        ValidateInputs(planet, targetOrbit);

        if (rocket.InitialMass <= 0.0 || rocket.Thrust <= 0.0 || rocket.Isp <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(rocket), "Rocket mass, thrust, and Isp must be positive.");
        }

        var geometry = BuildOrbitGeometry(planet, targetOrbit, latitude);
        var surfaceGravity = planet.Mu / (planet.Radius * planet.Radius);
        var initialTwr = rocket.Thrust / (rocket.InitialMass * surfaceGravity);
        var idealDeltaV = geometry.IdealSurfaceDeltaV;
        var dryMass = rocket.InitialMass * DefaultDryMassFraction;
        var availableDeltaV = rocket.Isp * StandardGravity * Math.Log(rocket.InitialMass / dryMass);

        // A rocket-aware gravity-loss heuristic is more stable here than the old scalar Euler integration.
        var gravityLossPercent = Clamp(
            0.06,
            0.26,
            EstimateVacuumGravityLossPercent(surfaceGravity, targetOrbit, initialTwr) - (0.08 * Math.Max(0.0, initialTwr - 1.20)));
        var gravityLosses = idealDeltaV * gravityLossPercent;
        var expendedDeltaV = idealDeltaV + gravityLosses;
        var netAcceleration = Math.Max(0.05, (rocket.Thrust / (rocket.InitialMass * 0.65)) - (0.72 * surfaceGravity));
        var timeToOrbit = expendedDeltaV / netAcceleration;

        if (initialTwr <= 1.02)
        {
            var stalledFraction = Math.Clamp(initialTwr / 1.02, 0.05, 0.95);
            return new AscentResult
            {
                TimeToOrbit = timeToOrbit,
                FinalAltitude = targetOrbit.PeriapsisAltitude * stalledFraction,
                FinalVelocity = geometry.TargetVelocityAtPeriapsis * stalledFraction,
                IdealDeltaV = idealDeltaV,
                ExpendedDeltaV = expendedDeltaV,
                GravityLosses = gravityLosses,
                DragLosses = 0.0,
                Successful = false,
                IsEstimate = false
            };
        }

        var successful = availableDeltaV >= expendedDeltaV;
        var successFraction = successful
            ? 1.0
            : Math.Clamp(availableDeltaV / Math.Max(expendedDeltaV, 1.0), 0.0, 0.99);

        return new AscentResult
        {
            TimeToOrbit = timeToOrbit,
            FinalAltitude = targetOrbit.PeriapsisAltitude * successFraction,
            FinalVelocity = geometry.TargetVelocityAtPeriapsis * successFraction,
            IdealDeltaV = idealDeltaV,
            ExpendedDeltaV = expendedDeltaV,
            GravityLosses = gravityLosses,
            DragLosses = 0.0,
            Successful = successful,
            IsEstimate = false
        };
    }

    public static AscentResult EstimateAscentQuick(
        PlanetParams planet,
        TargetOrbit targetOrbit,
        double latitude = 0.0)
    {
        ValidateInputs(planet, targetOrbit);

        var geometry = BuildOrbitGeometry(planet, targetOrbit, latitude);
        var surfaceGravity = planet.Mu / (planet.Radius * planet.Radius);
        var idealDeltaV = geometry.IdealSurfaceDeltaV;
        var gravityLosses = idealDeltaV * EstimateVacuumGravityLossPercent(surfaceGravity, targetOrbit, twr: null);

        return new AscentResult
        {
            TimeToOrbit = 0.0,
            FinalAltitude = targetOrbit.PeriapsisAltitude,
            FinalVelocity = geometry.TargetVelocityAtPeriapsis,
            IdealDeltaV = idealDeltaV,
            ExpendedDeltaV = idealDeltaV + gravityLosses,
            GravityLosses = gravityLosses,
            DragLosses = 0.0,
            Successful = true,
            IsEstimate = true
        };
    }

    public static PlanetParams CreatePlanetFromBody(OrbitalBody body)
    {
        var rotationPeriod = body.RotationPeriodSeconds ?? 86_400.0;
        var hasAtmosphere =
            !body.Name.Contains("Mars", StringComparison.OrdinalIgnoreCase) &&
            !body.Name.Contains("Moon", StringComparison.OrdinalIgnoreCase) &&
            !body.Name.Contains("Mercury", StringComparison.OrdinalIgnoreCase);

        return new PlanetParams
        {
            Name = body.Name,
            Mu = body.GravitationalParameter,
            Radius = body.Radius,
            RotationPeriod = rotationPeriod,
            HasAtmosphere = hasAtmosphere
        };
    }

    public static string GetResultSummary(
        PlanetParams planet,
        TargetOrbit orbit,
        AscentResult result)
    {
        var lines = new System.Collections.Generic.List<string>();
        var modeLabel = result.IsEstimate ? "[quick]" : "[rocket-aware]";

        lines.Add($"=== {modeLabel} Surface to Orbit: {planet.Name} ===");
        lines.Add($"Target orbit:     {(orbit.PeriapsisAltitude / 1000.0):0.0} x {(orbit.ApoapsisAltitude / 1000.0):0.0} km");
        lines.Add($"Inclination:      {orbit.Inclination:0.0} deg");

        if (result.Successful)
        {
            lines.Add($"Status:           success");
            lines.Add($"Required dV:      {result.ExpendedDeltaV:0.0} m/s");
            lines.Add($"Ideal dV:         {result.IdealDeltaV:0.0} m/s");
            lines.Add($"Gravity losses:   {result.GravityLosses:0.0} m/s");
            if (result.TimeToOrbit > 0.0)
            {
                lines.Add($"Time to orbit:    {result.TimeToOrbit:0.0} s");
            }
        }
        else
        {
            lines.Add($"Status:           failed");
            lines.Add($"Reached altitude: {(result.FinalAltitude / 1000.0):0.0} km");
            lines.Add($"Reached velocity: {result.FinalVelocity:0.0} m/s");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void ValidateInputs(PlanetParams planet, TargetOrbit targetOrbit)
    {
        if (planet.Mu <= 0.0 || planet.Radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(planet), "Planet radius and gravitational parameter must be positive.");
        }

        if (targetOrbit.PeriapsisAltitude < 0.0 || targetOrbit.ApoapsisAltitude < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetOrbit), "Orbit altitudes cannot be negative.");
        }

        if (targetOrbit.ApoapsisAltitude < targetOrbit.PeriapsisAltitude)
        {
            throw new ArgumentOutOfRangeException(nameof(targetOrbit), "Orbit apoapsis altitude cannot be lower than periapsis altitude.");
        }
    }

    private static (double TargetVelocityAtPeriapsis, double IdealSurfaceDeltaV) BuildOrbitGeometry(
        PlanetParams planet,
        TargetOrbit targetOrbit,
        double latitude)
    {
        var rPer = planet.Radius + targetOrbit.PeriapsisAltitude;
        var rApo = planet.Radius + targetOrbit.ApoapsisAltitude;
        var semiMajorAxis = (rPer + rApo) * 0.5;
        var targetVelocityAtPeriapsis = Math.Sqrt(planet.Mu * (2.0 / rPer - 1.0 / semiMajorAxis));
        var requiredSurfaceSpeed = Math.Sqrt(Math.Max(0.0, (2.0 * planet.Mu / planet.Radius) - (planet.Mu / semiMajorAxis)));
        var rotationBonus = ComputeEffectiveRotationBonus(planet, targetOrbit, latitude);
        var idealSurfaceDeltaV = Math.Max(0.0, requiredSurfaceSpeed - rotationBonus);

        return (targetVelocityAtPeriapsis, idealSurfaceDeltaV);
    }

    private static double ComputeEffectiveRotationBonus(PlanetParams planet, TargetOrbit targetOrbit, double latitude)
    {
        var equatorialBonus = planet.EquatorialVelocity * Math.Cos(latitude * Math.PI / 180.0);
        var minimumReachableInclination = Math.Abs(latitude);
        var inclinationGap = Math.Max(0.0, Math.Abs(targetOrbit.Inclination) - minimumReachableInclination);
        var inclinationPenaltyScale = Math.Cos(Math.Min(90.0, inclinationGap) * Math.PI / 180.0);
        return equatorialBonus * inclinationPenaltyScale;
    }

    private static double EstimateVacuumGravityLossPercent(double surfaceGravity, TargetOrbit targetOrbit, double? twr)
    {
        var periapsisAltitudeKm = targetOrbit.PeriapsisAltitude / 1000.0;
        var eccentricityTerm = targetOrbit.ApoapsisAltitude > 0.0
            ? (targetOrbit.ApoapsisAltitude - targetOrbit.PeriapsisAltitude) / Math.Max(targetOrbit.ApoapsisAltitude, 1.0)
            : 0.0;
        var baseLossPercent = 0.06 + (0.012 * surfaceGravity) + Math.Min(0.03, periapsisAltitudeKm / 25_000.0) + (0.03 * eccentricityTerm);

        if (!twr.HasValue)
        {
            return Clamp(0.05, 0.24, baseLossPercent);
        }

        var twrAdjustment = 0.0;
        if (twr.Value < 1.15)
        {
            twrAdjustment = 0.05 * (1.15 - twr.Value);
        }
        else if (twr.Value > 1.80)
        {
            twrAdjustment = -0.015 * Math.Min(1.0, twr.Value - 1.80);
        }

        return Clamp(0.05, 0.26, baseLossPercent + twrAdjustment);
    }

    private static double Clamp(double min, double max, double value)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
