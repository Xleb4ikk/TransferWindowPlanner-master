namespace TrajectoryCalculator.Tests;

public class LambertSolverTests
{
    private const double SunMu = 1.3271244004127942e20;



    [Fact]
    public void Solve_WithOutputVelocity_VelocitiesAreFinite()
    {
        var earthPos = new Vector3D(149_597_870_700, 0, 0);
        var marsPos = new Vector3D(-100_000_000_000, 150_000_000_000, 10_000_000_000);
        var tof = 220 * 86_400;

        var v1 = LambertSolver.Solve(SunMu, earthPos, marsPos, tof, false, out var v2);
        Assert.True(v1.Magnitude > 0);
        Assert.True(v2.Magnitude > 0);
        Assert.True(double.IsFinite(v1.Magnitude));
        Assert.True(double.IsFinite(v2.Magnitude));
    }
}
