namespace StandaloneTrajectoryCalculator;

public sealed class CentralBody
{
    public CentralBody(string name, double gravitationalParameter)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Central body name is required.", nameof(name));
        }

        if (gravitationalParameter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gravitationalParameter), "Gravitational parameter must be positive.");
        }

        Name = name;
        GravitationalParameter = gravitationalParameter;
    }

    public string Name { get; }
    public double GravitationalParameter { get; }
}

public sealed class OrbitalBody
{
    public OrbitalBody(
        string name,
        double gravitationalParameter,
        double radius,
        double? sphereOfInfluence,
        OrbitalElements orbit,
        double? rotationPeriodSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Body name is required.", nameof(name));
        }

        if (gravitationalParameter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gravitationalParameter), "Gravitational parameter must be positive.");
        }

        if (radius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");
        }

        Name = name;
        GravitationalParameter = gravitationalParameter;
        Radius = radius;
        SphereOfInfluence = sphereOfInfluence;
        Orbit = orbit ?? throw new ArgumentNullException(nameof(orbit));
        RotationPeriodSeconds = rotationPeriodSeconds;
    }

    public string Name { get; }
    public double GravitationalParameter { get; }
    public double Radius { get; }
    public double? SphereOfInfluence { get; }
    public OrbitalElements Orbit { get; }
    public double? RotationPeriodSeconds { get; }
}

public sealed class SecularOrbitTerms
{
    public SecularOrbitTerms(
        double semiMajorAxisRatePerCentury,
        double eccentricityRatePerCentury,
        double inclinationRateRadiansPerCentury,
        double longitudeOfAscendingNodeRateRadiansPerCentury,
        double longitudeOfPeriapsisRadiansAtEpoch,
        double longitudeOfPeriapsisRateRadiansPerCentury,
        double meanLongitudeRadiansAtEpoch,
        double meanLongitudeRateRadiansPerCentury,
        double meanAnomalyQuadraticTermRadiansPerCentury2,
        double meanAnomalyCosineTermRadians,
        double meanAnomalySineTermRadians,
        double meanAnomalyFrequencyRadiansPerCentury)
    {
        SemiMajorAxisRatePerCentury = semiMajorAxisRatePerCentury;
        EccentricityRatePerCentury = eccentricityRatePerCentury;
        InclinationRateRadiansPerCentury = inclinationRateRadiansPerCentury;
        LongitudeOfAscendingNodeRateRadiansPerCentury = longitudeOfAscendingNodeRateRadiansPerCentury;
        LongitudeOfPeriapsisRadiansAtEpoch = longitudeOfPeriapsisRadiansAtEpoch;
        LongitudeOfPeriapsisRateRadiansPerCentury = longitudeOfPeriapsisRateRadiansPerCentury;
        MeanLongitudeRadiansAtEpoch = meanLongitudeRadiansAtEpoch;
        MeanLongitudeRateRadiansPerCentury = meanLongitudeRateRadiansPerCentury;
        MeanAnomalyQuadraticTermRadiansPerCentury2 = meanAnomalyQuadraticTermRadiansPerCentury2;
        MeanAnomalyCosineTermRadians = meanAnomalyCosineTermRadians;
        MeanAnomalySineTermRadians = meanAnomalySineTermRadians;
        MeanAnomalyFrequencyRadiansPerCentury = meanAnomalyFrequencyRadiansPerCentury;
    }

    public double SemiMajorAxisRatePerCentury { get; }
    public double EccentricityRatePerCentury { get; }
    public double InclinationRateRadiansPerCentury { get; }
    public double LongitudeOfAscendingNodeRateRadiansPerCentury { get; }
    public double LongitudeOfPeriapsisRadiansAtEpoch { get; }
    public double LongitudeOfPeriapsisRateRadiansPerCentury { get; }
    public double MeanLongitudeRadiansAtEpoch { get; }
    public double MeanLongitudeRateRadiansPerCentury { get; }
    public double MeanAnomalyQuadraticTermRadiansPerCentury2 { get; }
    public double MeanAnomalyCosineTermRadians { get; }
    public double MeanAnomalySineTermRadians { get; }
    public double MeanAnomalyFrequencyRadiansPerCentury { get; }
}

public sealed class OrbitalElements
{
    public OrbitalElements(
        double semiMajorAxis,
        double eccentricity,
        double inclinationRadians,
        double longitudeOfAscendingNodeRadians,
        double argumentOfPeriapsisRadians,
        double meanAnomalyAtEpochRadians,
        double epoch,
        double parentGravitationalParameter,
        SecularOrbitTerms? secularTerms = null)
    {
        if (semiMajorAxis <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(semiMajorAxis), "Semi-major axis must be positive.");
        }

        if (eccentricity < 0 || eccentricity >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(eccentricity), "Only elliptical orbits with 0 <= e < 1 are supported.");
        }

        if (parentGravitationalParameter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentGravitationalParameter), "Parent gravitational parameter must be positive.");
        }

        SemiMajorAxis = semiMajorAxis;
        Eccentricity = eccentricity;
        InclinationRadians = inclinationRadians;
        LongitudeOfAscendingNodeRadians = longitudeOfAscendingNodeRadians;
        ArgumentOfPeriapsisRadians = argumentOfPeriapsisRadians;
        MeanAnomalyAtEpochRadians = meanAnomalyAtEpochRadians;
        Epoch = epoch;
        ParentGravitationalParameter = parentGravitationalParameter;
        SecularTerms = secularTerms;
    }

    public double SemiMajorAxis { get; }
    public double Eccentricity { get; }
    public double InclinationRadians { get; }
    public double LongitudeOfAscendingNodeRadians { get; }
    public double ArgumentOfPeriapsisRadians { get; }
    public double MeanAnomalyAtEpochRadians { get; }
    public double Epoch { get; }
    public double ParentGravitationalParameter { get; }
    public SecularOrbitTerms? SecularTerms { get; }
    public bool UsesSecularTerms => SecularTerms is not null;

    public double Period => LambertSolver.TwoPi * Math.Sqrt(Math.Pow(SemiMajorAxis, 3) / ParentGravitationalParameter);
    public double MeanMotion => Math.Sqrt(ParentGravitationalParameter / Math.Pow(SemiMajorAxis, 3));

    public double TrueAnomalyAtTime(double universalTime)
    {
        if (UsesSecularTerms)
        {
            var current = ResolveInstantaneousElements(universalTime);
            return current.TrueAnomalyRadians;
        }

        var meanAnomaly = NormalizeAngle(MeanAnomalyAtEpochRadians + MeanMotion * (universalTime - Epoch));
        if (Eccentricity == 0)
        {
            return meanAnomaly;
        }

        var eccentricAnomaly = SolveEccentricAnomaly(meanAnomaly, Eccentricity);
        return TrueAnomalyFromEccentricAnomaly(eccentricAnomaly, Eccentricity);
    }

    public Vector3D PositionAtTime(double universalTime)
    {
        if (UsesSecularTerms)
        {
            return PositionFromInstantaneousElements(ResolveInstantaneousElements(universalTime));
        }

        return PositionAtTrueAnomaly(TrueAnomalyAtTime(universalTime));
    }

    public Vector3D VelocityAtTime(double universalTime)
    {
        if (UsesSecularTerms)
        {
            var step = GetSecularVelocityStep();
            var before = PositionFromInstantaneousElements(ResolveInstantaneousElements(universalTime - step));
            var after = PositionFromInstantaneousElements(ResolveInstantaneousElements(universalTime + step));
            return (after - before) / (2.0 * step);
        }

        return VelocityAtTrueAnomaly(TrueAnomalyAtTime(universalTime));
    }

    public Vector3D PositionAtTrueAnomaly(double trueAnomaly)
    {
        var cos = Math.Cos(trueAnomaly);
        var sin = Math.Sin(trueAnomaly);
        var p = SemiMajorAxis * (1 - Eccentricity * Eccentricity);
        var radius = p / (1 + Eccentricity * cos);
        return RotateFromPerifocal(
            new Vector3D(radius * cos, radius * sin, 0),
            LongitudeOfAscendingNodeRadians,
            InclinationRadians,
            ArgumentOfPeriapsisRadians);
    }

    public Vector3D VelocityAtTrueAnomaly(double trueAnomaly)
    {
        var cos = Math.Cos(trueAnomaly);
        var sin = Math.Sin(trueAnomaly);
        var p = SemiMajorAxis * (1 - Eccentricity * Eccentricity);
        var scale = Math.Sqrt(ParentGravitationalParameter / p);
        return RotateFromPerifocal(
            new Vector3D(-scale * sin, scale * (Eccentricity + cos), 0),
            LongitudeOfAscendingNodeRadians,
            InclinationRadians,
            ArgumentOfPeriapsisRadians);
    }

    private InstantaneousElements ResolveInstantaneousElements(double universalTime)
    {
        if (SecularTerms is null)
        {
            var fixedMeanAnomaly = NormalizeAngle(MeanAnomalyAtEpochRadians + MeanMotion * (universalTime - Epoch));
            var fixedEccentricAnomaly = SolveEccentricAnomaly(fixedMeanAnomaly, Eccentricity);
            return new InstantaneousElements(
                SemiMajorAxis,
                Eccentricity,
                InclinationRadians,
                LongitudeOfAscendingNodeRadians,
                ArgumentOfPeriapsisRadians,
                fixedMeanAnomaly,
                fixedEccentricAnomaly,
                TrueAnomalyFromEccentricAnomaly(fixedEccentricAnomaly, Eccentricity));
        }

        var centuries = (universalTime - Epoch) / SolarSystemCatalog.SecondsPerJulianCentury;
        var semiMajorAxis = SemiMajorAxis + SecularTerms.SemiMajorAxisRatePerCentury * centuries;
        var eccentricity = Eccentricity + SecularTerms.EccentricityRatePerCentury * centuries;
        var inclination = InclinationRadians + SecularTerms.InclinationRateRadiansPerCentury * centuries;
        var longitudeOfAscendingNode = LongitudeOfAscendingNodeRadians +
                                       SecularTerms.LongitudeOfAscendingNodeRateRadiansPerCentury * centuries;
        var longitudeOfPeriapsis = SecularTerms.LongitudeOfPeriapsisRadiansAtEpoch +
                                   SecularTerms.LongitudeOfPeriapsisRateRadiansPerCentury * centuries;
        var meanLongitude = SecularTerms.MeanLongitudeRadiansAtEpoch +
                            SecularTerms.MeanLongitudeRateRadiansPerCentury * centuries;
        var argumentOfPeriapsis = NormalizeAngle(longitudeOfPeriapsis - longitudeOfAscendingNode);
        var meanAnomaly = NormalizeSignedAngle(
            meanLongitude -
            longitudeOfPeriapsis +
            SecularTerms.MeanAnomalyQuadraticTermRadiansPerCentury2 * centuries * centuries +
            SecularTerms.MeanAnomalyCosineTermRadians * Math.Cos(SecularTerms.MeanAnomalyFrequencyRadiansPerCentury * centuries) +
            SecularTerms.MeanAnomalySineTermRadians * Math.Sin(SecularTerms.MeanAnomalyFrequencyRadiansPerCentury * centuries));

        if (semiMajorAxis <= 0)
        {
            throw new InvalidOperationException("Instantaneous semi-major axis became non-positive.");
        }

        if (eccentricity < 0 || eccentricity >= 1)
        {
            throw new InvalidOperationException("Instantaneous eccentricity is outside the supported elliptical range.");
        }

        var eccentricAnomaly = SolveEccentricAnomaly(meanAnomaly, eccentricity);
        return new InstantaneousElements(
            semiMajorAxis,
            eccentricity,
            inclination,
            longitudeOfAscendingNode,
            argumentOfPeriapsis,
            meanAnomaly,
            eccentricAnomaly,
            TrueAnomalyFromEccentricAnomaly(eccentricAnomaly, eccentricity));
    }

    private Vector3D PositionFromInstantaneousElements(InstantaneousElements elements)
    {
        var cos = Math.Cos(elements.EccentricAnomalyRadians);
        var sin = Math.Sin(elements.EccentricAnomalyRadians);
        var x = elements.SemiMajorAxis * (cos - elements.Eccentricity);
        var y = elements.SemiMajorAxis * Math.Sqrt(1 - elements.Eccentricity * elements.Eccentricity) * sin;
        return RotateFromPerifocal(
            new Vector3D(x, y, 0),
            elements.LongitudeOfAscendingNodeRadians,
            elements.InclinationRadians,
            elements.ArgumentOfPeriapsisRadians);
    }

    private double GetSecularVelocityStep()
    {
        return Math.Clamp(Period / 100_000.0, 30.0, 3_600.0);
    }

    private static Vector3D RotateFromPerifocal(
        Vector3D perifocal,
        double longitudeOfAscendingNodeRadians,
        double inclinationRadians,
        double argumentOfPeriapsisRadians)
    {
        var cosLan = Math.Cos(longitudeOfAscendingNodeRadians);
        var sinLan = Math.Sin(longitudeOfAscendingNodeRadians);
        var cosI = Math.Cos(inclinationRadians);
        var sinI = Math.Sin(inclinationRadians);
        var cosArg = Math.Cos(argumentOfPeriapsisRadians);
        var sinArg = Math.Sin(argumentOfPeriapsisRadians);

        var m11 = cosLan * cosArg - sinLan * sinArg * cosI;
        var m12 = -cosLan * sinArg - sinLan * cosArg * cosI;
        var m21 = sinLan * cosArg + cosLan * sinArg * cosI;
        var m22 = -sinLan * sinArg + cosLan * cosArg * cosI;
        var m31 = sinArg * sinI;
        var m32 = cosArg * sinI;

        return new Vector3D(
            m11 * perifocal.X + m12 * perifocal.Y,
            m21 * perifocal.X + m22 * perifocal.Y,
            m31 * perifocal.X + m32 * perifocal.Y);
    }

    private static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
    {
        var eccentricAnomaly = eccentricity < 0.8 ? meanAnomaly : Math.PI;

        for (var i = 0; i < 30; i++)
        {
            var f = eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly;
            var derivative = 1 - eccentricity * Math.Cos(eccentricAnomaly);
            var delta = f / derivative;
            eccentricAnomaly -= delta;

            if (Math.Abs(delta) < 1e-12)
            {
                return NormalizeAngle(eccentricAnomaly);
            }
        }

        throw new InvalidOperationException("Kepler equation failed to converge for orbit propagation.");
    }

    private static double TrueAnomalyFromEccentricAnomaly(double eccentricAnomaly, double eccentricity)
    {
        if (eccentricity == 0)
        {
            return NormalizeAngle(eccentricAnomaly);
        }

        var cosTrue = (Math.Cos(eccentricAnomaly) - eccentricity) /
                      (1 - eccentricity * Math.Cos(eccentricAnomaly));
        var sinTrue = Math.Sqrt(1 - eccentricity * eccentricity) * Math.Sin(eccentricAnomaly) /
                      (1 - eccentricity * Math.Cos(eccentricAnomaly));
        return NormalizeAngle(Math.Atan2(sinTrue, cosTrue));
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= LambertSolver.TwoPi;
        if (angle < 0)
        {
            angle += LambertSolver.TwoPi;
        }

        return angle;
    }

    private static double NormalizeSignedAngle(double angle)
    {
        angle = NormalizeAngle(angle);
        if (angle > Math.PI)
        {
            angle -= LambertSolver.TwoPi;
        }

        return angle;
    }

    private readonly record struct InstantaneousElements(
        double SemiMajorAxis,
        double Eccentricity,
        double InclinationRadians,
        double LongitudeOfAscendingNodeRadians,
        double ArgumentOfPeriapsisRadians,
        double MeanAnomalyRadians,
        double EccentricAnomalyRadians,
        double TrueAnomalyRadians);
}
