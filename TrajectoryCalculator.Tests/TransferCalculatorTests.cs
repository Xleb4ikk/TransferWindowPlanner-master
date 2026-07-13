namespace TrajectoryCalculator.Tests;

public class TransferCalculatorTests
{
    private static readonly OrbitalBody Earth = SolarSystemCatalog.CreateOrbitalBody("Earth");
    private static readonly OrbitalBody Mars = SolarSystemCatalog.CreateOrbitalBody("Mars");
    private static readonly CentralBody Sun = new("Sun", SolarSystemCatalog.SunGravitationalParameter);

    [Fact]
    public void EarthMars_ReturnsReasonableDeltaV()
    {
        var result = TransferCalculator.CalculateTransfer(
            Earth, Mars, Sun,
            departureTime: SolarSystemCatalog.ToJ2000Seconds(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)),
            travelTime: 220 * SolarSystemCatalog.SecondsPerDay,
            departureParkingOrbitPeriapsisAltitude: 200_000.0,
            departureParkingOrbitApoapsisAltitude: 200_000.0,
            arrivalParkingOrbitAltitude: 250_000.0,
            arrivalManeuverMode: "circular-capture",
            useAerobraking: false,
            longWayOverride: null);

        Assert.NotNull(result);
        Assert.InRange(result.DVTotal, 7_000, 10_000);
        Assert.InRange(result.DVEjection, 4_000, 6_000);
        Assert.InRange(result.DVInjection, 2_000, 5_000);
    }

    [Fact]
    public void EarthMars_Deterministic()
    {
        var r1 = TransferCalculator.CalculateTransfer(Earth, Mars, Sun, 0, 100 * SolarSystemCatalog.SecondsPerDay, 200_000, 200_000, 250_000, "circular-capture", false, null);
        var r2 = TransferCalculator.CalculateTransfer(Earth, Mars, Sun, 0, 100 * SolarSystemCatalog.SecondsPerDay, 200_000, 200_000, 250_000, "circular-capture", false, null);

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(r1.DVTotal, r2.DVTotal);
    }

    [Fact]
    public void ZeroTravelTime_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransferCalculator.CalculateTransfer(Earth, Mars, Sun, 0, 0, 200_000, 200_000, 250_000, "circular-capture", false, null));
    }

    [Fact]
    public void NegativeParkingOrbit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransferCalculator.CalculateTransfer(Earth, Mars, Sun, 0, 86_400, -1, 200_000, 250_000, "circular-capture", false, null));
    }


}
