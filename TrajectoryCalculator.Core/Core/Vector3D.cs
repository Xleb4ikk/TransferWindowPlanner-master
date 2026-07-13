using System.Globalization;
using System.Text.Json.Serialization;

namespace TrajectoryCalculator;

/// <summary>
/// 3D vector for positions, velocities, and other physical quantities.
/// </summary>
public readonly struct Vector3D
{
    public Vector3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    [JsonIgnore]
    public double SqrMagnitude => X * X + Y * Y + Z * Z;

    [JsonIgnore]
    public double Magnitude => Math.Sqrt(SqrMagnitude);

    [JsonIgnore]
    public Vector3D Normalized => Magnitude == 0 ? Zero : this / Magnitude;

    public static Vector3D Zero => new(0, 0, 0);

    public static Vector3D operator +(Vector3D left, Vector3D right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vector3D operator -(Vector3D left, Vector3D right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vector3D operator -(Vector3D value) =>
        new(-value.X, -value.Y, -value.Z);

    public static Vector3D operator *(Vector3D value, double scalar) =>
        new(value.X * scalar, value.Y * scalar, value.Z * scalar);

    public static Vector3D operator *(double scalar, Vector3D value) => value * scalar;

    public static Vector3D operator /(Vector3D value, double scalar) =>
        new(value.X / scalar, value.Y / scalar, value.Z / scalar);

    public static double Dot(Vector3D left, Vector3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    public static Vector3D Cross(Vector3D left, Vector3D right) =>
        new(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);

    public static double AngleRadians(Vector3D left, Vector3D right)
    {
        var denominator = left.Magnitude * right.Magnitude;
        if (denominator == 0)
        {
            return 0;
        }

        var ratio = Math.Clamp(Dot(left, right) / denominator, -1.0, 1.0);
        return Math.Acos(ratio);
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"({X:G9}, {Y:G9}, {Z:G9})");
}
