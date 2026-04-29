namespace StandaloneTrajectoryCalculator;

public static class LambertSolver
{
    public const double TwoPi = 2.0d * Math.PI;
    public const double Deg2Rad = Math.PI / 180.0d;
    public const double Rad2Deg = 180.0d / Math.PI;

    public static Vector3D Solve(double gravitationalParameter, Vector3D position1, Vector3D position2, double timeOfFlight, bool longWay)
    {
        Vector3D velocity2;
        return Solve(gravitationalParameter, position1, position2, timeOfFlight, longWay, out velocity2);
    }

    public static Vector3D Solve(double gravitationalParameter, Vector3D position1, Vector3D position2, double timeOfFlight, bool longWay, out Vector3D velocity2)
    {
        if (gravitationalParameter <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gravitationalParameter), "Gravitational parameter must be positive.");
        }

        if (timeOfFlight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeOfFlight), "Time of flight must be positive.");
        }

        var r1 = position1.Magnitude;
        var r2 = position2.Magnitude;
        if (r1 <= 0 || r2 <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position1), "Position vectors must be non-zero.");
        }

        var cosDelta = Math.Clamp(Vector3D.Dot(position1, position2) / (r1 * r2), -1.0, 1.0);
        var sinDeltaMagnitude = Math.Sqrt(Math.Max(0.0, 1.0 - cosDelta * cosDelta));
        var sinDelta = longWay ? -sinDeltaMagnitude : sinDeltaMagnitude;
        var oneMinusCosDelta = 1.0 - cosDelta;

        if (oneMinusCosDelta < 1e-12 || Math.Abs(sinDelta) < 1e-12)
        {
            throw new InvalidOperationException("Lambert transfer angle is singular.");
        }

        var transferParameter = sinDelta * Math.Sqrt(r1 * r2 / oneMinusCosDelta);
        if (Math.Abs(transferParameter) < 1e-12)
        {
            throw new InvalidOperationException("Lambert transfer parameter is singular.");
        }

        var z = FindUniversalVariable(
            gravitationalParameter,
            r1,
            r2,
            transferParameter,
            timeOfFlight);

        if (!TryUniversalGeometry(r1, r2, transferParameter, z, out var y, out _, out _))
        {
            throw new InvalidOperationException("Lambert geometry is not physically valid.");
        }

        var f = 1.0 - y / r1;
        var g = transferParameter * Math.Sqrt(y / gravitationalParameter);
        var gDot = 1.0 - y / r2;

        if (Math.Abs(g) < 1e-9)
        {
            throw new InvalidOperationException("Lambert Lagrange coefficient is singular.");
        }

        var velocity1 = (position2 - position1 * f) / g;
        velocity2 = (position2 * gDot - position1) / g;
        return velocity1;
    }

    private static double FindUniversalVariable(
        double gravitationalParameter,
        double r1,
        double r2,
        double transferParameter,
        double targetTimeOfFlight)
    {
        var sqrtMu = Math.Sqrt(gravitationalParameter);
        var targetTolerance = Math.Max(1e-3, targetTimeOfFlight * 1e-10);

        var bracket = FindUniversalVariableBracket(
            sqrtMu,
            r1,
            r2,
            transferParameter,
            targetTimeOfFlight,
            targetTolerance);

        var lower = bracket.Lower;
        var upper = bracket.Upper;
        var fLower = bracket.FLower;

        for (var i = 0; i < 120; i++)
        {
            var mid = 0.5 * (lower + upper);
            if (!TryUniversalTimeOfFlight(sqrtMu, r1, r2, transferParameter, mid, out var midTime))
            {
                lower = mid;
                continue;
            }

            var fMid = midTime - targetTimeOfFlight;
            if (Math.Abs(fMid) <= targetTolerance || Math.Abs(upper - lower) < 1e-12)
            {
                return mid;
            }

            if (Math.Sign(fMid) == Math.Sign(fLower))
            {
                lower = mid;
                fLower = fMid;
            }
            else
            {
                upper = mid;
            }
        }

        return 0.5 * (lower + upper);
    }

    private static UniversalBracket FindUniversalVariableBracket(
        double sqrtMu,
        double r1,
        double r2,
        double transferParameter,
        double targetTimeOfFlight,
        double targetTolerance)
    {
        if (TryUniversalTimeOfFlight(sqrtMu, r1, r2, transferParameter, 0.0, out var zeroTime))
        {
            var fZero = zeroTime - targetTimeOfFlight;
            if (Math.Abs(fZero) <= targetTolerance)
            {
                return new UniversalBracket(0.0, 0.0, fZero, fZero);
            }

            if (fZero > 0.0)
            {
                var upper = 0.0;
                var fUpper = fZero;
                var lower = -1.0;

                for (var i = 0; i < 80; i++)
                {
                    if (TryUniversalTimeOfFlight(sqrtMu, r1, r2, transferParameter, lower, out var lowerTime))
                    {
                        var fLower = lowerTime - targetTimeOfFlight;
                        if (fLower <= 0.0)
                        {
                            return new UniversalBracket(lower, upper, fLower, fUpper);
                        }
                    }

                    lower *= 2.0;
                }
            }
            else
            {
                var lower = 0.0;
                var fLower = fZero;
                var upperLimit = 4.0 * Math.PI * Math.PI - 1e-8;
                var upper = 1.0;

                for (var i = 0; i < 80 && upper < upperLimit; i++)
                {
                    if (TryUniversalTimeOfFlight(sqrtMu, r1, r2, transferParameter, upper, out var upperTime))
                    {
                        var fUpper = upperTime - targetTimeOfFlight;
                        if (fUpper >= 0.0)
                        {
                            return new UniversalBracket(lower, upper, fLower, fUpper);
                        }
                    }

                    upper = Math.Min(upper * 2.0, upperLimit);
                }
            }
        }

        return ScanUniversalVariableBracket(
            sqrtMu,
            r1,
            r2,
            transferParameter,
            targetTimeOfFlight);
    }

    private static UniversalBracket ScanUniversalVariableBracket(
        double sqrtMu,
        double r1,
        double r2,
        double transferParameter,
        double targetTimeOfFlight)
    {
        const int samples = 512;
        var lowerLimit = -64.0 * Math.PI * Math.PI;
        var upperLimit = 4.0 * Math.PI * Math.PI - 1e-8;
        var hasPrevious = false;
        var previousZ = 0.0;
        var previousF = 0.0;

        for (var i = 0; i <= samples; i++)
        {
            var z = lowerLimit + (upperLimit - lowerLimit) * i / samples;
            if (!TryUniversalTimeOfFlight(sqrtMu, r1, r2, transferParameter, z, out var timeOfFlight))
            {
                continue;
            }

            var f = timeOfFlight - targetTimeOfFlight;
            if (Math.Abs(f) <= Math.Max(1e-3, targetTimeOfFlight * 1e-10))
            {
                return new UniversalBracket(z, z, f, f);
            }

            if (hasPrevious && Math.Sign(f) != Math.Sign(previousF))
            {
                return new UniversalBracket(previousZ, z, previousF, f);
            }

            hasPrevious = true;
            previousZ = z;
            previousF = f;
        }

        throw new InvalidOperationException("Lambert time-of-flight root could not be bracketed.");
    }

    private static bool TryUniversalTimeOfFlight(
        double sqrtMu,
        double r1,
        double r2,
        double transferParameter,
        double z,
        out double timeOfFlight)
    {
        timeOfFlight = 0.0;
        if (!TryUniversalGeometry(r1, r2, transferParameter, z, out var y, out var c, out var s))
        {
            return false;
        }

        var x = Math.Sqrt(y / c);
        timeOfFlight = (x * x * x * s + transferParameter * Math.Sqrt(y)) / sqrtMu;
        return double.IsFinite(timeOfFlight);
    }

    private static bool TryUniversalGeometry(
        double r1,
        double r2,
        double transferParameter,
        double z,
        out double y,
        out double c,
        out double s)
    {
        c = StumpffC(z);
        s = StumpffS(z);
        y = 0.0;

        if (!double.IsFinite(c) || !double.IsFinite(s) || c <= 0.0)
        {
            return false;
        }

        y = r1 + r2 + transferParameter * (z * s - 1.0) / Math.Sqrt(c);
        return double.IsFinite(y) && y > 0.0;
    }

    private static double StumpffC(double z)
    {
        if (z > 1e-8)
        {
            var sqrtZ = Math.Sqrt(z);
            return (1.0 - Math.Cos(sqrtZ)) / z;
        }

        if (z < -1e-8)
        {
            var sqrtNegativeZ = Math.Sqrt(-z);
            return (Math.Cosh(sqrtNegativeZ) - 1.0) / -z;
        }

        return 0.5 - z / 24.0 + z * z / 720.0 - z * z * z / 40_320.0;
    }

    private static double StumpffS(double z)
    {
        if (z > 1e-8)
        {
            var sqrtZ = Math.Sqrt(z);
            return (sqrtZ - Math.Sin(sqrtZ)) / (sqrtZ * sqrtZ * sqrtZ);
        }

        if (z < -1e-8)
        {
            var sqrtNegativeZ = Math.Sqrt(-z);
            return (Math.Sinh(sqrtNegativeZ) - sqrtNegativeZ) /
                   (sqrtNegativeZ * sqrtNegativeZ * sqrtNegativeZ);
        }

        return 1.0 / 6.0 - z / 120.0 + z * z / 5_040.0 - z * z * z / 362_880.0;
    }

    private readonly record struct UniversalBracket(double Lower, double Upper, double FLower, double FUpper);
}
