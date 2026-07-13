# TrajectoryCalculator API Reference

## Quick Start

```csharp
using TrajectoryCalculator;

var planner = new TrajectoryPlanner();
var result = planner.Calculate(SampleScenarioFactory.CreateSingleTemplate());
Console.WriteLine(result.Transfer?.DVTotal);
```

## Installation

```
dotnet add package TrajectoryCalculator.Core
```

## Thread Safety

- `TrajectoryPlanner` is stateless and safe as a Singleton.
- `ScenarioInput` is not mutated during calculation.
- `ExecutionResult` is immutable after creation.
- Static calculators (`TransferCalculator`, `LambertSolver`, etc.) are thread-safe.

## High-level API

| API | Stability | Description |
|-----|-----------|-------------|
| `ITrajectoryCalculator` | Stable | Interface for trajectory calculation |
| `TrajectoryPlanner` | Stable | Default implementation of `ITrajectoryCalculator` |
| `IBatchCalculator` | Stable | Interface for batch processing |
| `BatchProcessor` | Stable | Default implementation of `IBatchCalculator` |
| `TrajectoryCalculatorOptions` | Stable | Future extension point for options |

## Low-level API

| API | Stability | Description |
|-----|-----------|-------------|
| `ScenarioExecutor` | Stable | Executes a scenario with full control |
| `TransferCalculator` | Stable | Single transfer calculation |
| `LambertSolver` | Advanced | Lambert's problem solver (low-level) |
| `BatchCalculator` | Stable | Static batch processing (low-level) |
| `PorkchopCalculator` | Advanced | Porkchop plot scan |
| `SurfaceLaunchTimingCalculator` | Advanced | Surface launch window analysis |
| `OrbitalAscentCalculator` | Advanced | Orbital ascent simulation |

## Models

| API | Description |
|-----|-------------|
| `ScenarioInput` | Complete scenario: calendar, central body, bodies, request |
| `ExecutionResult` | Result: summary, transfer details, launch analysis |
| `TransferDetails` | Computed transfer: delta-V, positions, velocities |
| `PorkchopResult` | Porkchop grid scan result |
| `PorkchopPoint` | Single point in a porkchop grid |
| `LaunchAnalysis` | Launch ascent analysis |
| `SurfaceLaunchTiming` | Surface launch timing details |
| `CentralBody` | Central gravitational body |
| `OrbitalBody` | Orbiting body with elements |
| `Vector3D` | 3D vector for positions and velocities |
| `BatchItemResult` | Result of a single batch item |
| `BatchRunResult` | Aggregate batch result |

## Utilities

| API | Description |
|-----|-------------|
| `ScenarioJson` | JSON serialization/deserialization |
| `SampleScenarioFactory` | Pre-built scenario templates (Earth→Mars) |
| `SolarSystemCatalog` | JPL-based Solar System ephemeris catalog |
| `MissionCalendar` | Calendar arithmetic for mission dates |

## Dependency Injection

```csharp
using TrajectoryCalculator;

services.AddTrajectoryCalculator();
```

Registers `ITrajectoryCalculator` and `IBatchCalculator` as Singletons.

## Examples

See the `samples/` directory:
- `Sample01_HighLevel` — high-level API usage
- `Sample02_FromFile` — file-based calculation
- `Sample03_LowLevel` — direct low-level API usage
