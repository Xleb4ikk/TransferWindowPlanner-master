namespace TrajectoryCalculator.Tests;

public class SolarSystemCatalogTests
{
    [Fact]
    public void PlanetNames_ContainsKnownPlanets()
    {
        var names = SolarSystemCatalog.PlanetNames;
        Assert.Contains("Earth", names);
        Assert.Contains("Mars", names);
        Assert.Contains("Jupiter", names);
        Assert.Contains("Venus", names);
        Assert.Contains("Mercury", names);
        Assert.Contains("Saturn", names);
        Assert.Contains("Uranus", names);
        Assert.Contains("Neptune", names);
    }

    [Fact]
    public void Constants_ArePositive()
    {
        Assert.True(SolarSystemCatalog.AstronomicalUnit > 0);
        Assert.True(SolarSystemCatalog.SunGravitationalParameter > 0);
        Assert.True(SolarSystemCatalog.SecondsPerDay > 0);
    }

    [Fact]
    public void CreateBodyInput_KnownPlanet_ReturnsBody()
    {
        var earth = SolarSystemCatalog.CreateBodyInput("Earth");
        Assert.NotNull(earth);
        Assert.Equal("Earth", earth.Name);
        Assert.True(earth.GravitationalParameter > 0);
        Assert.True(earth.Radius > 0);
        Assert.NotNull(earth.Orbit);
    }

    [Fact]
    public void CreateBodyInput_UnknownPlanet_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SolarSystemCatalog.CreateBodyInput("Pluto"));
    }

    [Fact]
    public void CreateOrbitalBody_Earth_IsValid()
    {
        var earth = SolarSystemCatalog.CreateOrbitalBody("Earth");
        Assert.NotNull(earth);
        Assert.Equal("Earth", earth.Name);
        Assert.True(earth.GravitationalParameter > 0);
        Assert.True(earth.Radius > 0);
        Assert.True(earth.SphereOfInfluence > 0);
    }
}
