namespace TrajectoryCalculator.Tests;

public class SampleScenarioFactoryTests
{
    [Fact]
    public void SingleTemplate_IsValid()
    {
        var input = SampleScenarioFactory.CreateSingleTemplate();
        Assert.NotNull(input);
        Assert.NotNull(input.Calendar);
        Assert.NotNull(input.CentralBody);
        Assert.NotNull(input.Bodies);
        Assert.NotEmpty(input.Bodies);
        Assert.NotNull(input.Request);
        Assert.Equal("single", input.Request.Mode);
        Assert.False(string.IsNullOrWhiteSpace(input.Request.Origin));
        Assert.False(string.IsNullOrWhiteSpace(input.Request.Destination));
        Assert.True(input.Request.DepartureTime.HasValue);
        Assert.True(input.Request.TravelTime.HasValue);
    }

    [Fact]
    public void PorkchopTemplate_IsValid()
    {
        var input = SampleScenarioFactory.CreatePorkchopTemplate();
        Assert.NotNull(input);
        Assert.NotNull(input.Calendar);
        Assert.NotNull(input.CentralBody);
        Assert.NotEmpty(input.Bodies);
        Assert.NotNull(input.Request);
        Assert.Equal("porkchop", input.Request.Mode);
        Assert.True(input.Request.DepartureWindowStart.HasValue);
        Assert.True(input.Request.DepartureWindowEnd.HasValue);
        Assert.True(input.Request.TravelTimeMin.HasValue);
        Assert.True(input.Request.TravelTimeMax.HasValue);
    }
}
