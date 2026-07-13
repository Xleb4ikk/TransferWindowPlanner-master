using Microsoft.Extensions.DependencyInjection;

namespace TrajectoryCalculator;

/// <summary>
/// DI registration extensions for TrajectoryCalculator services.
/// </summary>
public static class TrajectoryCalculatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITrajectoryCalculator"/> and <see cref="IBatchCalculator"/> as singletons.
    /// </summary>
    public static IServiceCollection AddTrajectoryCalculator(this IServiceCollection services)
    {
        services.AddSingleton<ITrajectoryCalculator, TrajectoryPlanner>();
        services.AddSingleton<IBatchCalculator, BatchProcessor>();
        return services;
    }
}
