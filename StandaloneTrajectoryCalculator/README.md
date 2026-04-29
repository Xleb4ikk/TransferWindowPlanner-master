# Standalone Trajectory Calculator

Independent interplanetary-transfer calculator extracted from the old KSP mod logic and rebuilt without KSP or Unity dependencies.

## Features

- Lambert solver for direct transfer computation
- porkchop scan over departure and flight-time windows
- built-in Solar System presets for Mercury through Neptune
- UTC/J2000 mission timing
- departure and arrival parking-orbit support
- JSON input/output and CSV export for porkchop data

## Built-In Solar System Data

The default templates now use:

- Sun as the central body
- UTC calendar with epoch `2000-01-01T12:00:00Z`
- JPL long-term planetary orbital element series
- physical radii and gravitational parameters for the real planets

Earth uses Earth physical constants together with the JPL Earth/EM-bary orbital series.

## Usage

Write a sample single-transfer scenario:

```powershell
dotnet run --project .\StandaloneTrajectoryCalculator -- template single .\StandaloneTrajectoryCalculator\sample-single.json
```

Write a sample porkchop scenario:

```powershell
dotnet run --project .\StandaloneTrajectoryCalculator -- template porkchop .\StandaloneTrajectoryCalculator\sample-porkchop.json
```

Run a scenario:

```powershell
dotnet run --project .\StandaloneTrajectoryCalculator -- .\StandaloneTrajectoryCalculator\sample-single.json
dotnet run --project .\StandaloneTrajectoryCalculator -- .\StandaloneTrajectoryCalculator\sample-porkchop.json
dotnet run --project .\StandaloneTrajectoryCalculator -- .\examples\scenarios\transfer\earth-to-jupiter-elliptic.json
```

Generate a synthetic chemical-propulsion training dataset:

```powershell
dotnet run --project .\StandaloneTrajectoryCalculator -- dataset fuel .\data\datasets\fuel-dataset 300 12345 32
```

Run the GUI:

```powershell
dotnet run --project .\StandaloneTrajectoryCalculator.Gui
```

## Input Notes

- `calendar.kind = "utc"` enables UTC formatting and J2000-style mission dates.
- `bodies[].orbit` can now include secular-rate fields such as `semiMajorAxisRate`, `meanLongitudeDeg`, and `longitudeOfPeriapsisDeg`.
- `request.arrivalParkingOrbitAltitude`:
  - `null` means do not compute arrival burn
  - `0` means flyby
  - `> 0` means arrival burn at the chosen periapsis altitude
- `request.arrivalManeuverMode` when `arrivalParkingOrbitAltitude > 0`:
  - `"elliptic-capture"` means minimum capture into a very high elliptical orbit inside the SOI
  - `"circular-capture"` means direct circularization at the chosen altitude
  - default is `"circular-capture"`
- `request.departureTime` is the parking-orbit departure burn time in J2000 seconds.
- When `request.launch.enabled = true`, the solver now also estimates a compatible surface launch window from the departure vector, the chosen parking-orbit inclination, the body rotation period, and the launch site latitude/longitude.
- `request.launch.launchLongitude` is the launch-site east longitude in degrees at solver time `t = 0` (J2000); default is `0`.

## Accuracy Note

The built-in Solar System model is much closer to real planetary motion than the old fixed toy templates, but it is still an approximation model based on fitted JPL element series rather than a full n-body integrated ephemeris. For mission-grade precision, JPL Horizons or SPICE is still the better source.

When you use parking-orbit departure or arrival burns, the reported ejection/insertion geometry is computed with a patched-conics sphere-of-influence model. That is a good approximation for the standalone solver, but Principia users should treat those parking-orbit burn angles as a starting guess rather than an exact n-body maneuver prescription.

The `dataset fuel` command generates a synthetic ML dataset on top of the transfer solver. It samples valid interplanetary missions, evaluates several common chemical propellant families, estimates stage mass and cryogenic boil-off, and writes:

- `mission_dataset.csv` with mission features and the best synthetic departure/arrival propellant labels
- `architecture_dataset.csv` with evaluated multi-stage rocket architectures
- `metadata.json` with the assumptions used for dataset generation

The current generator builds rockets with 2 to 5 total stages. Lower departure stages can use aggressive cryogenic propellants, while the top cruise/arrival stage is evaluated separately with long-duration storage penalties so the dataset can learn when boil-off makes that choice unattractive.

The last CLI argument limits how many feasible architectures per mission are written to `architecture_dataset.csv`. Use `0` to keep every evaluated architecture.

This dataset is intended as a bootstrap training corpus for model development. The propellant figures, stage splits, and boil-off behavior are conservative engineering priors rather than flight-certified stage design data.

Additional curated scenarios now live under `examples/scenarios/`, while generated run output is intended for `data/results/`.
