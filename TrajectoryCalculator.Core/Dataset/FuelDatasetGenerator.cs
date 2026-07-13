using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TrajectoryCalculator;

public sealed class FuelDatasetRunResult
{
    public required string OutputDirectory { get; init; }
    public required string MissionDatasetPath { get; init; }
    public required string ArchitectureDatasetPath { get; init; }
    public required string MetadataPath { get; init; }
    public required int MissionCount { get; init; }
    public required int ArchitectureCount { get; init; }
}

public sealed class FuelDatasetOptions
{
    public string OutputDirectory { get; init; } = Path.GetFullPath("data/datasets/fuel-dataset");
    public int MissionCount { get; init; } = 300;
    public int Seed { get; init; } = 12345;
    public int StartYearUtc { get; init; } = 2026;
    public int EndYearUtc { get; init; } = 2045;
    public double MinPayloadMassKg { get; init; } = 250.0;
    public double MaxPayloadMassKg { get; init; } = 100_000.0;
    public int MinStageCount { get; init; } = 2;
    public int MaxStageCount { get; init; } = 5;
    public int MaxArchitecturesPerMissionOutput { get; init; } = 32;
    public int MaxMissionAttempts { get; init; } = 0;
}

internal sealed record StageProfile(
    int StageNumber,
    int TotalStageCount,
    string Role,
    MissionSegment Segment,
    double DeltaVMps,
    double StorageDays,
    double SolarFluxRelative,
    IReadOnlyList<ChemicalPropellant> CandidatePropellants);

internal sealed record MissionSample(
    int MissionId,
    string OriginName,
    string DestinationName,
    string DepartureUtc,
    double DepartureTimeJ2000Seconds,
    double TravelDays,
    double PayloadMassKg,
    double DepartureParkingOrbitKm,
    double ArrivalParkingOrbitKm,
    TransferDetails Transfer,
    double DepartureDistanceAu,
    double ArrivalDistanceAu,
    double MeanDistanceAu,
    double MinDistanceAu,
    double DepartureSolarFluxWm2,
    double MeanSolarFluxWm2,
    double CorrectionReserveDvMps,
    double DepartureStorageDays,
    double ArrivalStorageDays);

internal sealed record StageMassEstimate(
    int StageNumber,
    int TotalStageCount,
    string Role,
    ChemicalPropellant Propellant,
    RocketEngine? Engine,
    int EngineCount,
    MissionSegment Segment,
    double DeltaVMps,
    double PayloadAfterBurnKg,
    double StorageDays,
    double SolarFluxRelative,
    double EffectiveIspSeconds,
    double EffectiveTankageFactor,
    double BoiloffFraction,
    double LoadedPropellantMassKg,
    double BurnPropellantMassKg,
    double BoiloffMassKg,
    double TankMassKg,
    double StructureMassKg,
    double TotalEngineMassKg,
    double DryMassKg,
    double InitialMassKg,
    double FinalMassKg,
    double AvailableThrustkN,
    double PenaltyMassKg,
    bool IsFeasible,
    string InfeasibleReason);

internal sealed class ArchitectureEvaluation
{
    public required MissionSample Mission { get; init; }
    public required IReadOnlyList<StageMassEstimate> Stages { get; init; }
    public required double ScoreKgEquivalent { get; init; }
    public double ArchitectureRealismPenaltyKg { get; init; }
    public required bool IsFeasible { get; init; }
    public bool IsBest { get; set; }
    public int OutputRank { get; set; }
    public int StageCount => Stages.Count;
    public string ArchitectureKey => string.Join("__", Stages.Select(stage => stage.Propellant.Key));
    public double LaunchMassKg => Stages.Count == 0 ? 0.0 : Stages[0].InitialMassKg;
    public double TotalLoadedPropellantKg => Stages.Sum(stage => stage.LoadedPropellantMassKg);
    public double TotalBurnPropellantKg => Stages.Sum(stage => stage.BurnPropellantMassKg);
    public double TotalBoiloffMassKg => Stages.Sum(stage => stage.BoiloffMassKg);
    public double TotalTankMassKg => Stages.Sum(stage => stage.TankMassKg);
    public double TotalStructureMassKg => Stages.Sum(stage => stage.StructureMassKg);
    public double TotalEngineMassKg => Stages.Sum(stage => stage.TotalEngineMassKg);
    public double TotalDryMassKg => Stages.Sum(stage => stage.DryMassKg);
    public double TankCarryProxyKg => Stages.Sum(stage => stage.TankMassKg * stage.StageNumber);
    public double TotalPenaltyKg => Stages.Sum(stage => stage.PenaltyMassKg) + ArchitectureRealismPenaltyKg;
}

public static class FuelDatasetGenerator
{
    private const double StandardGravity = 9.80665;
    private const double SolarFluxAtOneAu = 1361.0;
    private const int SupportedMinStageCount = 2;
    private const int SupportedMaxStageCount = 5;
    private const int MaxStageColumns = 5;
    private const double DefaultMaxMissionDvMps = 30_000.0;
    private const double OuterPlanetMaxMissionDvMps = 45_000.0;

    private static readonly IReadOnlyList<string> DatasetPlanetNames = SolarSystemCatalog.PlanetNames
        .Where(name => !string.Equals(name, "Neptune", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private static readonly IReadOnlyDictionary<int, double[]> DepartureDvFractionsByStageCount =
        new Dictionary<int, double[]>
        {
            [1] = [1.00],
            [2] = [0.58, 0.42],
            [3] = [0.46, 0.32, 0.22],
            [4] = [0.38, 0.27, 0.20, 0.15]
        };

    private static readonly IReadOnlyList<ChemicalPropellant> FirstDepartureStagePropellants =
    [
        ChemicalPropellantCatalog.Get("lox_rp1"),
        ChemicalPropellantCatalog.Get("lox_ch4"),
        ChemicalPropellantCatalog.Get("lox_lh2")
    ];

    private static readonly IReadOnlyList<ChemicalPropellant> MiddleDepartureStagePropellants =
    [
        ChemicalPropellantCatalog.Get("lox_ch4"),
        ChemicalPropellantCatalog.Get("lox_lh2"),
        ChemicalPropellantCatalog.Get("nto_mmh")
    ];

    private static readonly IReadOnlyList<ChemicalPropellant> UpperDepartureStagePropellants =
    [
        ChemicalPropellantCatalog.Get("lox_lh2"),
        ChemicalPropellantCatalog.Get("lox_ch4"),
        ChemicalPropellantCatalog.Get("nto_mmh")
    ];

    private static readonly IReadOnlyList<ChemicalPropellant> CruiseArrivalStagePropellants =
    [
        ChemicalPropellantCatalog.Get("nto_mmh"),
        ChemicalPropellantCatalog.Get("lox_ch4"),
        ChemicalPropellantCatalog.Get("hydrazine"),
        ChemicalPropellantCatalog.Get("lox_lh2")
    ];

    public static FuelDatasetRunResult Generate(string outputDirectory, int missionCount, int seed)
    {
        return Generate(new FuelDatasetOptions
        {
            OutputDirectory = Path.GetFullPath(outputDirectory),
            MissionCount = missionCount,
            Seed = seed
        });
    }

    public static FuelDatasetRunResult Generate(FuelDatasetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MissionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Mission count must be positive.");
        }

        if (options.MinPayloadMassKg <= 0 || options.MaxPayloadMassKg <= options.MinPayloadMassKg)
        {
            throw new InvalidOperationException("Payload mass range is invalid.");
        }

        if (options.MinStageCount < SupportedMinStageCount || options.MaxStageCount > SupportedMaxStageCount || options.MinStageCount > options.MaxStageCount)
        {
            throw new InvalidOperationException($"Stage count range must stay within {SupportedMinStageCount}..{SupportedMaxStageCount}.");
        }

        var rng = new Random(options.Seed);
        var missions = new List<MissionSample>(options.MissionCount);
        var bestArchitectures = new List<ArchitectureEvaluation>(options.MissionCount);
        var architectureCount = 0;

        var maxMissionAttempts = ResolveMaxMissionAttempts(options);
        Directory.CreateDirectory(options.OutputDirectory);
        var missionDatasetPath = Path.Combine(options.OutputDirectory, "mission_dataset.csv");
        var architectureDatasetPath = Path.Combine(options.OutputDirectory, "architecture_dataset.csv");
        var metadataPath = Path.Combine(options.OutputDirectory, "metadata.json");

        using var architectureWriter = CreateArchitectureDatasetWriter(architectureDatasetPath);

        for (var attempts = 0; missions.Count < options.MissionCount; attempts++)
        {
            if (attempts >= maxMissionAttempts)
            {
                throw new InvalidOperationException($"Could only synthesize {missions.Count} valid missions after {attempts} attempts. Increase the attempt budget or narrow the sampling ranges.");
            }

            if (!TryCreateMission(missions.Count + 1, rng, options, out var mission))
            {
                continue;
            }

            var missionArchitectures = EvaluateArchitectures(mission, options);
            var bestArchitecture = missionArchitectures
                .Where(architecture => architecture.IsFeasible)
                .OrderBy(architecture => architecture.ScoreKgEquivalent)
                .FirstOrDefault();

            if (bestArchitecture is null)
            {
                continue;
            }

            bestArchitecture.IsBest = true;
            var retainedArchitectures = SelectArchitecturesForOutput(missionArchitectures, options.MaxArchitecturesPerMissionOutput);
            bestArchitecture.OutputRank = 1;
            missions.Add(mission);
            bestArchitectures.Add(bestArchitecture);
            WriteArchitectureRows(architectureWriter, retainedArchitectures);
            architectureCount += retainedArchitectures.Count;
        }

        WriteMissionDataset(missionDatasetPath, bestArchitectures);
        WriteMetadata(metadataPath, options, missions.Count, architectureCount, maxMissionAttempts);

        return new FuelDatasetRunResult
        {
            OutputDirectory = options.OutputDirectory,
            MissionDatasetPath = missionDatasetPath,
            ArchitectureDatasetPath = architectureDatasetPath,
            MetadataPath = metadataPath,
            MissionCount = missions.Count,
            ArchitectureCount = architectureCount
        };
    }

    private static bool TryCreateMission(int missionId, Random rng, FuelDatasetOptions options, out MissionSample mission)
    {
        mission = null!;

        var originName = PickPlanet(rng, null);
        var destinationName = PickPlanet(rng, originName);
        var origin = SolarSystemCatalog.CreateOrbitalBody(originName);
        var destination = SolarSystemCatalog.CreateOrbitalBody(destinationName);
        var centralBody = new CentralBody("Sun", SolarSystemCatalog.SunGravitationalParameter);

        var startUtc = new DateTimeOffset(options.StartYearUtc, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endUtc = new DateTimeOffset(options.EndYearUtc, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var departureUtc = startUtc.AddDays(rng.NextDouble() * (endUtc - startUtc).TotalDays);
        var departureTime = SolarSystemCatalog.ToJ2000Seconds(departureUtc);

        var hohmannDays = TransferCalculator.HohmannTimeOfFlight(origin, destination) / SolarSystemCatalog.SecondsPerDay;
        var departureParkingOrbitKm = SampleParkingOrbitKm(origin, rng, isArrival: false);
        var arrivalParkingOrbitKm = SampleParkingOrbitKm(destination, rng, isArrival: true);
        var payloadMassKg = SampleLogUniform(rng, options.MinPayloadMassKg, options.MaxPayloadMassKg);
        var departureStorageDays = SampleDepartureStorageDays(rng, originName, destinationName);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var travelFactor = SampleTravelFactor(rng, originName, destinationName);
            var travelDays = Math.Max(20.0, hohmannDays * travelFactor);
            var longWayOverride = SampleLongWayOverride(rng);
            var transfer = TransferCalculator.CalculateTransfer(
                origin,
                destination,
                centralBody,
                departureTime,
                travelDays * SolarSystemCatalog.SecondsPerDay,
                departureParkingOrbitKm * 1000.0,
                departureParkingOrbitKm * 1000.0,
                arrivalParkingOrbitKm * 1000.0,
                "elliptic-capture",
                false,
                longWayOverride);

            if (transfer is null)
            {
                continue;
            }

            if (transfer.DVTotal <= 100.0 || transfer.DVTotal > ResolveMaxMissionDvMps(originName, destinationName))
            {
                continue;
            }

            var departureDistanceAu = transfer.OriginPositionAtDeparture.Magnitude / SolarSystemCatalog.AstronomicalUnit;
            var arrivalDistanceAu = transfer.DestinationPositionAtArrival.Magnitude / SolarSystemCatalog.AstronomicalUnit;
            var meanDistanceAu = (departureDistanceAu + arrivalDistanceAu) * 0.5;
            var minDistanceAu = Math.Min(departureDistanceAu, arrivalDistanceAu);
            var departureSolarFlux = SolarFluxAtOneAu / Math.Pow(Math.Max(0.2, departureDistanceAu), 2);
            var meanSolarFlux = SolarFluxAtOneAu / Math.Pow(Math.Max(0.2, meanDistanceAu), 2);
            var correctionReserveDv = Math.Clamp(transfer.DVTotal * 0.03, 25.0, 300.0);

            mission = new MissionSample(
                missionId,
                originName,
                destinationName,
                SolarSystemCatalog.FormatUtc(departureUtc),
                departureTime,
                travelDays,
                payloadMassKg,
                departureParkingOrbitKm,
                arrivalParkingOrbitKm,
                transfer,
                departureDistanceAu,
                arrivalDistanceAu,
                meanDistanceAu,
                minDistanceAu,
                departureSolarFlux,
                meanSolarFlux,
                correctionReserveDv,
                departureStorageDays,
                travelDays);
            return true;
        }

        return false;
    }

    private static List<ArchitectureEvaluation> EvaluateArchitectures(MissionSample mission, FuelDatasetOptions options)
    {
        var results = new List<ArchitectureEvaluation>(EstimateArchitecturesPerMission(options));

        for (var totalStageCount = options.MinStageCount; totalStageCount <= options.MaxStageCount; totalStageCount++)
        {
            var stageProfiles = BuildStageProfiles(mission, totalStageCount);
            var selectedPropellants = new ChemicalPropellant[stageProfiles.Count];
            EnumerateArchitectureChoices(mission, stageProfiles, selectedPropellants, 0, results);
        }

        return results;
    }

    private static void EnumerateArchitectureChoices(
        MissionSample mission,
        IReadOnlyList<StageProfile> stageProfiles,
        ChemicalPropellant[] selectedPropellants,
        int stageIndex,
        List<ArchitectureEvaluation> results)
    {
        if (stageIndex >= stageProfiles.Count)
        {
            results.Add(EvaluateArchitecture(mission, stageProfiles, selectedPropellants));
            return;
        }

        foreach (var propellant in stageProfiles[stageIndex].CandidatePropellants)
        {
            selectedPropellants[stageIndex] = propellant;
            EnumerateArchitectureChoices(mission, stageProfiles, selectedPropellants, stageIndex + 1, results);
        }
    }

    private static ArchitectureEvaluation EvaluateArchitecture(
        MissionSample mission,
        IReadOnlyList<StageProfile> stageProfiles,
        IReadOnlyList<ChemicalPropellant> selectedPropellants)
    {
        var stageEstimates = new StageMassEstimate[stageProfiles.Count];
        var payloadAfterBurnKg = mission.PayloadMassKg;

        for (var index = stageProfiles.Count - 1; index >= 0; index--)
        {
            var stage = stageProfiles[index];
            var estimate = EstimateStage(
                mission,
                selectedPropellants[index],
                stage.StageNumber,
                stage.TotalStageCount,
                stage.Role,
                stage.Segment,
                payloadAfterBurnKg,
                stage.DeltaVMps,
                stage.StorageDays,
                stage.SolarFluxRelative);
            stageEstimates[index] = estimate;
            payloadAfterBurnKg = estimate.InitialMassKg;
        }

        var feasible = stageEstimates.All(stage => stage.IsFeasible) && double.IsFinite(stageEstimates[0].InitialMassKg);
        var architectureRealismPenaltyKg = feasible
            ? ComputeArchitectureRealismPenaltyKg(mission, stageEstimates)
            : double.PositiveInfinity;
        var score = feasible
            ? stageEstimates[0].InitialMassKg + stageEstimates.Sum(stage => stage.PenaltyMassKg) + architectureRealismPenaltyKg
            : double.PositiveInfinity;

        return new ArchitectureEvaluation
        {
            Mission = mission,
            Stages = stageEstimates,
            ScoreKgEquivalent = score,
            ArchitectureRealismPenaltyKg = feasible ? architectureRealismPenaltyKg : 0.0,
            IsFeasible = feasible
        };
    }

    private static double ComputeArchitectureRealismPenaltyKg(
        MissionSample mission,
        IReadOnlyList<StageMassEstimate> stageEstimates)
    {
        var departureStages = stageEstimates.Where(stage => stage.Segment == MissionSegment.Departure).ToArray();
        if (departureStages.Length == 0)
        {
            return 0.0;
        }

        var departureDifficultyScale = 1.0;
        if (mission.Transfer.DVTotal >= 18_000.0)
        {
            departureDifficultyScale = 0.65;
        }
        else if (mission.Transfer.DVTotal >= 14_000.0)
        {
            departureDifficultyScale = 0.80;
        }

        var departureComplexityPenaltyKg = 0.0;
        if (departureStages.Length >= 3)
        {
            departureComplexityPenaltyKg += 500.0 * Math.Pow(departureStages.Length - 2, 2);
            if (departureStages.Length >= 4)
            {
                departureComplexityPenaltyKg += 2_000.0;
            }
        }

        var hydroloxPenaltyKg = 0.0;
        var departureLh2Count = 0;
        var currentLh2Run = 0;
        var longestLh2Run = 0;
        foreach (var stage in departureStages)
        {
            if (!string.Equals(stage.Propellant.Key, "lox_lh2", StringComparison.OrdinalIgnoreCase))
            {
                currentLh2Run = 0;
                continue;
            }

            departureLh2Count++;
            currentLh2Run++;
            longestLh2Run = Math.Max(longestLh2Run, currentLh2Run);

            hydroloxPenaltyKg += stage.Role switch
            {
                "booster" => 1_500.0,
                "departure_mid" => 1_300.0,
                "departure_core" => 900.0,
                "departure_upper" when departureStages.Length >= 3 => 200.0,
                _ => 0.0
            };
        }

        var extraDepartureLh2 = Math.Max(0, departureLh2Count - 1);
        if (extraDepartureLh2 > 0)
        {
            hydroloxPenaltyKg += extraDepartureLh2 * 1_200.0;
            hydroloxPenaltyKg += Math.Max(0, extraDepartureLh2 - 1) * 700.0;
        }

        if (longestLh2Run >= 2)
        {
            hydroloxPenaltyKg += (longestLh2Run - 1) * 900.0;
            hydroloxPenaltyKg += Math.Max(0, longestLh2Run - 2) * 1_100.0;
        }

        if (departureStages.Length >= 4
            && departureStages.All(stage => stage.Propellant.Key is "lox_lh2" or "lox_ch4" or "lox_rp1"))
        {
            hydroloxPenaltyKg += 900.0;
        }

        var arrivalStoragePenaltyKg = 0.0;
        var arrivalStage = stageEstimates.Last();
        if (mission.TravelDays >= 180.0)
        {
            var longCoastSteps = 1 + (mission.TravelDays >= 300.0 ? 1 : 0) + (mission.TravelDays >= 600.0 ? 1 : 0);
            arrivalStoragePenaltyKg += arrivalStage.Propellant.Key switch
            {
                "lox_lh2" => longCoastSteps * 1_800.0,
                "lox_ch4" => longCoastSteps * 700.0,
                "lox_rp1" => longCoastSteps * 900.0,
                _ => 0.0
            };

            if (arrivalStage.Propellant.Key is "lox_lh2" or "lox_ch4" or "lox_rp1")
            {
                var thermalMargin = Math.Max(0.0, 0.95 - mission.MinDistanceAu);
                if (thermalMargin > 0.0)
                {
                    var thermalBase = arrivalStage.Propellant.Key switch
                    {
                        "lox_lh2" => 2_200.0,
                        "lox_ch4" => 900.0,
                        "lox_rp1" => 1_200.0,
                        _ => 0.0
                    };
                    arrivalStoragePenaltyKg += thermalBase * Math.Min(1.0, thermalMargin / 0.35);
                }
            }
        }

        return ((departureComplexityPenaltyKg + hydroloxPenaltyKg) * departureDifficultyScale) + arrivalStoragePenaltyKg;
    }

    private static IReadOnlyList<ArchitectureEvaluation> SelectArchitecturesForOutput(
        IReadOnlyList<ArchitectureEvaluation> missionArchitectures,
        int maxArchitecturesPerMissionOutput)
    {
        var feasibleArchitectures = missionArchitectures
            .Where(architecture => architecture.IsFeasible)
            .OrderBy(architecture => architecture.ScoreKgEquivalent)
            .ToList();

        if (maxArchitecturesPerMissionOutput > 0)
        {
            feasibleArchitectures = feasibleArchitectures
                .Take(maxArchitecturesPerMissionOutput)
                .ToList();
        }

        for (var index = 0; index < feasibleArchitectures.Count; index++)
        {
            feasibleArchitectures[index].OutputRank = index + 1;
        }

        return feasibleArchitectures;
    }

    private static StageMassEstimate EstimateStage(
        MissionSample mission,
        ChemicalPropellant propellant,
        int stageNumber,
        int totalStageCount,
        string role,
        MissionSegment segment,
        double payloadAfterBurnKg,
        double deltaVMps,
        double storageDays,
        double solarFluxRelative)
    {
        var engine = RocketEngineCatalog.SelectEngine(propellant.Key, role, segment);
        if (!double.IsFinite(payloadAfterBurnKg) || payloadAfterBurnKg <= 0.0)
        {
            return new StageMassEstimate(
                StageNumber: stageNumber,
                TotalStageCount: totalStageCount,
                Role: role,
                Propellant: propellant,
                Engine: engine,
                EngineCount: 0,
                Segment: segment,
                DeltaVMps: deltaVMps,
                PayloadAfterBurnKg: payloadAfterBurnKg,
                StorageDays: storageDays,
                SolarFluxRelative: solarFluxRelative,
                EffectiveIspSeconds: 0.0,
                EffectiveTankageFactor: 0.0,
                BoiloffFraction: 0.0,
                LoadedPropellantMassKg: 0.0,
                BurnPropellantMassKg: 0.0,
                BoiloffMassKg: 0.0,
                TankMassKg: 0.0,
                StructureMassKg: 0.0,
                TotalEngineMassKg: 0.0,
                DryMassKg: 0.0,
                InitialMassKg: double.PositiveInfinity,
                FinalMassKg: double.PositiveInfinity,
                AvailableThrustkN: 0.0,
                PenaltyMassKg: 0.0,
                IsFeasible: false,
                InfeasibleReason: "blocked_by_upper_stage");
        }

        if (engine is null)
        {
            return new StageMassEstimate(
                StageNumber: stageNumber,
                TotalStageCount: totalStageCount,
                Role: role,
                Propellant: propellant,
                Engine: null,
                EngineCount: 0,
                Segment: segment,
                DeltaVMps: deltaVMps,
                PayloadAfterBurnKg: payloadAfterBurnKg,
                StorageDays: storageDays,
                SolarFluxRelative: solarFluxRelative,
                EffectiveIspSeconds: 0.0,
                EffectiveTankageFactor: 0.0,
                BoiloffFraction: 0.0,
                LoadedPropellantMassKg: 0.0,
                BurnPropellantMassKg: 0.0,
                BoiloffMassKg: 0.0,
                TankMassKg: 0.0,
                StructureMassKg: 0.0,
                TotalEngineMassKg: 0.0,
                DryMassKg: 0.0,
                InitialMassKg: double.PositiveInfinity,
                FinalMassKg: double.PositiveInfinity,
                AvailableThrustkN: 0.0,
                PenaltyMassKg: double.PositiveInfinity,
                IsFeasible: false,
                InfeasibleReason: "no_compatible_engine");
        }

        var effectiveTankageFactor = propellant.EffectiveTankageFactor(storageDays);
        var boiloffFraction = propellant.ComputeBoiloffFraction(storageDays, solarFluxRelative);
        if (boiloffFraction >= 0.95)
        {
            return new StageMassEstimate(
                StageNumber: stageNumber,
                TotalStageCount: totalStageCount,
                Role: role,
                Propellant: propellant,
                Engine: engine,
                EngineCount: 0,
                Segment: segment,
                DeltaVMps: deltaVMps,
                PayloadAfterBurnKg: payloadAfterBurnKg,
                StorageDays: storageDays,
                SolarFluxRelative: solarFluxRelative,
                EffectiveIspSeconds: engine.VacuumIspSeconds,
                EffectiveTankageFactor: effectiveTankageFactor,
                BoiloffFraction: boiloffFraction,
                LoadedPropellantMassKg: 0.0,
                BurnPropellantMassKg: 0.0,
                BoiloffMassKg: 0.0,
                TankMassKg: 0.0,
                StructureMassKg: 0.0,
                TotalEngineMassKg: 0.0,
                DryMassKg: 0.0,
                InitialMassKg: double.PositiveInfinity,
                FinalMassKg: double.PositiveInfinity,
                AvailableThrustkN: 0.0,
                PenaltyMassKg: double.PositiveInfinity,
                IsFeasible: false,
                InfeasibleReason: "boiloff_limit");
        }

        var structureMassKg = ComputeStageStructureMassKg(payloadAfterBurnKg, role, segment);
        var engineCount = 1;
        double loadedPropellantMassKg = 0.0;
        double burnPropellantMassKg = 0.0;
        double boiloffMassKg = 0.0;
        double tankMassKg = 0.0;
        double totalEngineMassKg = 0.0;
        double dryMassKg = 0.0;
        double initialMassKg = double.PositiveInfinity;
        double finalMassKg = double.PositiveInfinity;
        double availableThrustkN = 0.0;
        var massSolved = false;
        var infeasibleReason = string.Empty;

        for (var iteration = 0; iteration < 12; iteration++)
        {
            totalEngineMassKg = engineCount * engine.DryMassKg;
            var fixedDryMassKg = structureMassKg + totalEngineMassKg;
            var massRatio = Math.Exp(deltaVMps / (StandardGravity * engine.VacuumIspSeconds));
            var denominator = (1.0 - boiloffFraction) + effectiveTankageFactor * (1.0 - massRatio);
            if (denominator <= 1e-9)
            {
                infeasibleReason = "mass_ratio_or_boiloff_limit";
                break;
            }

            loadedPropellantMassKg = (payloadAfterBurnKg + fixedDryMassKg) * (massRatio - 1.0) / denominator;
            if (!double.IsFinite(loadedPropellantMassKg) || loadedPropellantMassKg <= 0.0)
            {
                infeasibleReason = "negative_stage_mass";
                break;
            }

            boiloffMassKg = loadedPropellantMassKg * boiloffFraction;
            burnPropellantMassKg = loadedPropellantMassKg - boiloffMassKg;
            tankMassKg = loadedPropellantMassKg * effectiveTankageFactor;
            dryMassKg = tankMassKg + fixedDryMassKg;
            initialMassKg = payloadAfterBurnKg + dryMassKg + loadedPropellantMassKg;
            finalMassKg = payloadAfterBurnKg + dryMassKg;
            availableThrustkN = engineCount * engine.VacuumThrustkN;

            var requiredEngineCount = ResolveEngineCount(role, segment, initialMassKg, engine);
            if (requiredEngineCount > engine.MaxClusterCount)
            {
                infeasibleReason = "cluster_limit";
                break;
            }

            if (requiredEngineCount == engineCount)
            {
                massSolved = true;
                break;
            }

            engineCount = requiredEngineCount;
        }

        if (!massSolved)
        {
            return new StageMassEstimate(
                stageNumber,
                totalStageCount,
                role,
                propellant,
                engine,
                engineCount,
                segment,
                deltaVMps,
                payloadAfterBurnKg,
                storageDays,
                solarFluxRelative,
                engine.VacuumIspSeconds,
                effectiveTankageFactor,
                boiloffFraction,
                loadedPropellantMassKg,
                burnPropellantMassKg,
                boiloffMassKg,
                tankMassKg,
                structureMassKg,
                totalEngineMassKg,
                double.PositiveInfinity,
                double.PositiveInfinity,
                double.PositiveInfinity,
                availableThrustkN,
                double.PositiveInfinity,
                false,
                string.IsNullOrWhiteSpace(infeasibleReason) ? "engine_sizing_failed" : infeasibleReason);
        }

        var penaltyMassKg = ComputeOperationalPenalty(mission, propellant, engine, segment, initialMassKg, storageDays, solarFluxRelative, deltaVMps, engineCount);

        var feasible = double.IsFinite(initialMassKg)
            && initialMassKg > 0.0
            && burnPropellantMassKg > 0.0
            && availableThrustkN > 0.0
            && initialMassKg / payloadAfterBurnKg <= 250.0;

        return new StageMassEstimate(
            stageNumber,
            totalStageCount,
            role,
            propellant,
            engine,
            engineCount,
            segment,
            deltaVMps,
            payloadAfterBurnKg,
            storageDays,
            solarFluxRelative,
            engine.VacuumIspSeconds,
            effectiveTankageFactor,
            boiloffFraction,
            loadedPropellantMassKg,
            burnPropellantMassKg,
            boiloffMassKg,
            tankMassKg,
            structureMassKg,
            totalEngineMassKg,
            dryMassKg,
            initialMassKg,
            finalMassKg,
            availableThrustkN,
            penaltyMassKg,
            feasible,
            feasible ? string.Empty : "stage_too_massive");
    }

    private static double ComputeOperationalPenalty(
        MissionSample mission,
        ChemicalPropellant propellant,
        RocketEngine engine,
        MissionSegment segment,
        double initialMassKg,
        double storageDays,
        double solarFluxRelative,
        double deltaVMps,
        int engineCount)
    {
        var penalty = initialMassKg * propellant.HandlingPenaltyFactor;

        if (segment == MissionSegment.Departure)
        {
            penalty += initialMassKg * propellant.PrimaryBurnPenaltyFactor * Math.Max(0.35, Math.Min(1.5, deltaVMps / 3500.0));
            if (propellant.IsLowThrust && (deltaVMps > 1200.0 || mission.PayloadMassKg > 3000.0))
            {
                penalty += initialMassKg * 0.35;
            }
        }
        else
        {
            penalty += initialMassKg * propellant.LongCoastPenaltyFactor * Math.Max(0.25, Math.Min(2.0, storageDays / 180.0)) * Math.Max(0.5, solarFluxRelative);
            penalty += initialMassKg * propellant.RestartPenaltyFactor;
            if (!engine.SupportsLongCoast)
            {
                penalty += initialMassKg * 0.06;
            }
        }

        if (propellant.IsHypergolic)
        {
            penalty += initialMassKg * 0.01;
        }

        if (!engine.SupportsRestart && segment == MissionSegment.Arrival)
        {
            penalty += initialMassKg * 0.08;
        }

        if (engineCount > 1)
        {
            penalty += initialMassKg * Math.Min(0.03, (engineCount - 1) * 0.0025);
        }

        if (propellant.IsCryogenic && segment == MissionSegment.Arrival && mission.MinDistanceAu < 1.0)
        {
            penalty += initialMassKg * (1.0 - mission.MinDistanceAu) * 0.10;
        }

        return penalty;
    }

    private static int ResolveEngineCount(string role, MissionSegment segment, double initialMassKg, RocketEngine engine)
    {
        var targetAcceleration = segment == MissionSegment.Arrival
            ? 0.35
            : role switch
            {
                "booster" => 1.10,
                "departure_core" => 1.00,
                "departure_mid" => 0.75,
                "departure_upper" => 0.45,
                _ => 0.60
            };

        var requiredThrustkN = initialMassKg * StandardGravity * targetAcceleration / 1000.0;
        return Math.Max(1, (int)Math.Ceiling(requiredThrustkN / Math.Max(1.0, engine.VacuumThrustkN)));
    }

    // Small fixed service mass keeps tiny extra stages from becoming unrealistically cheap.
    private static double ComputeStageStructureMassKg(double payloadAfterBurnKg, string role, MissionSegment segment)
    {
        var scaledMassKg = payloadAfterBurnKg * StageStructureFactor(role, segment);
        return scaledMassKg + StageFixedServiceMassKg(role, segment);
    }

    private static double StageFixedServiceMassKg(string role, MissionSegment segment)
    {
        if (segment == MissionSegment.Arrival)
        {
            return 85.0;
        }

        return role switch
        {
            "booster" => 320.0,
            "departure_core" => 280.0,
            "departure_mid" => 190.0,
            "departure_upper" => 140.0,
            _ => 180.0
        };
    }

    private static double StageStructureFactor(string role, MissionSegment segment)
    {
        if (segment == MissionSegment.Arrival)
        {
            return 0.010;
        }

        return role switch
        {
            "booster" => 0.018,
            "departure_mid" => 0.015,
            "departure_upper" => 0.012,
            _ => 0.016
        };
    }

    private static void WriteMissionDataset(string outputPath, IReadOnlyList<ArchitectureEvaluation> bestArchitectures)
    {
        var builder = new StringBuilder();
        var header = new List<string>
        {
            "mission_id",
            "origin",
            "destination",
            "departure_utc",
            "departure_time_j2000_s",
            "travel_days",
            "payload_mass_kg",
            "departure_orbit_km",
            "arrival_orbit_km",
            "departure_distance_au",
            "arrival_distance_au",
            "mean_distance_au",
            "min_distance_au",
            "departure_solar_flux_w_m2",
            "mean_solar_flux_w_m2",
            "dv_ejection_mps",
            "dv_injection_mps",
            "dv_total_mps",
            "phase_angle_deg",
            "transfer_angle_deg",
            "long_way",
            "correction_reserve_dv_mps",
            "best_architecture",
            "best_stage_count",
            "best_launch_mass_kg",
            "best_total_loaded_propellant_kg",
            "best_total_boiloff_mass_kg",
            "best_total_tank_mass_kg",
            "best_tank_carry_proxy_kg",
            "best_total_dry_mass_kg",
            "best_architecture_realism_penalty_kg",
            "best_total_penalty_kg"
        };
        AppendStageHeaders(header, "best_stage");
        AppendCsvRow(builder, header);

        foreach (var architecture in bestArchitectures.OrderBy(item => item.Mission.MissionId))
        {
            var mission = architecture.Mission;
            var fields = new List<string>
            {
                mission.MissionId.ToString(CultureInfo.InvariantCulture),
                mission.OriginName,
                mission.DestinationName,
                mission.DepartureUtc,
                Format(mission.DepartureTimeJ2000Seconds),
                Format(mission.TravelDays),
                Format(mission.PayloadMassKg),
                Format(mission.DepartureParkingOrbitKm),
                Format(mission.ArrivalParkingOrbitKm),
                Format(mission.DepartureDistanceAu),
                Format(mission.ArrivalDistanceAu),
                Format(mission.MeanDistanceAu),
                Format(mission.MinDistanceAu),
                Format(mission.DepartureSolarFluxWm2),
                Format(mission.MeanSolarFluxWm2),
                Format(mission.Transfer.DVEjection),
                Format(mission.Transfer.DVInjection),
                Format(mission.Transfer.DVTotal),
                Format(mission.Transfer.PhaseAngle * LambertSolver.Rad2Deg),
                Format(mission.Transfer.TransferAngle * LambertSolver.Rad2Deg),
                mission.Transfer.LongWay.ToString(),
                Format(mission.CorrectionReserveDvMps),
                architecture.ArchitectureKey,
                architecture.StageCount.ToString(CultureInfo.InvariantCulture),
                Format(architecture.LaunchMassKg),
                Format(architecture.TotalLoadedPropellantKg),
                Format(architecture.TotalBoiloffMassKg),
                Format(architecture.TotalTankMassKg),
                Format(architecture.TankCarryProxyKg),
                Format(architecture.TotalDryMassKg),
                Format(architecture.ArchitectureRealismPenaltyKg),
                Format(architecture.TotalPenaltyKg)
            };
            AppendStageFields(fields, architecture.Stages);
            AppendCsvRow(builder, fields);
        }

        File.WriteAllText(outputPath, builder.ToString());
    }

    private static StreamWriter CreateArchitectureDatasetWriter(string outputPath)
    {
        var header = new List<string>
        {
            "mission_id",
            "architecture_key",
            "stage_count",
            "output_rank",
            "is_best",
            "is_feasible",
            "score_kg_equivalent",
            "launch_mass_kg",
            "payload_mass_kg",
            "total_loaded_propellant_kg",
            "total_burn_propellant_kg",
            "total_boiloff_mass_kg",
            "total_tank_mass_kg",
            "total_structure_mass_kg",
            "total_engine_mass_kg",
            "total_dry_mass_kg",
            "tank_carry_proxy_kg",
            "architecture_realism_penalty_kg",
            "total_penalty_kg",
            "dv_ejection_mps",
            "dv_arrival_plus_reserve_mps"
        };
        AppendStageHeaders(header, "stage");
        var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        var builder = new StringBuilder();
        AppendCsvRow(builder, header);
        writer.Write(builder.ToString());
        return writer;
    }

    private static void WriteArchitectureRows(TextWriter writer, IReadOnlyList<ArchitectureEvaluation> architectures)
    {
        var builder = new StringBuilder();

        foreach (var architecture in architectures)
        {
            var fields = new List<string>
            {
                architecture.Mission.MissionId.ToString(CultureInfo.InvariantCulture),
                architecture.ArchitectureKey,
                architecture.StageCount.ToString(CultureInfo.InvariantCulture),
                architecture.OutputRank.ToString(CultureInfo.InvariantCulture),
                architecture.IsBest.ToString(),
                architecture.IsFeasible.ToString(),
                architecture.IsFeasible ? Format(architecture.ScoreKgEquivalent) : string.Empty,
                architecture.IsFeasible ? Format(architecture.LaunchMassKg) : string.Empty,
                Format(architecture.Mission.PayloadMassKg),
                Format(architecture.TotalLoadedPropellantKg),
                Format(architecture.TotalBurnPropellantKg),
                Format(architecture.TotalBoiloffMassKg),
                Format(architecture.TotalTankMassKg),
                Format(architecture.TotalStructureMassKg),
                Format(architecture.TotalEngineMassKg),
                Format(architecture.TotalDryMassKg),
                Format(architecture.TankCarryProxyKg),
                Format(architecture.ArchitectureRealismPenaltyKg),
                Format(architecture.TotalPenaltyKg),
                Format(architecture.Mission.Transfer.DVEjection),
                Format(architecture.Mission.Transfer.DVInjection + architecture.Mission.CorrectionReserveDvMps)
            };
            AppendStageFields(fields, architecture.Stages);
            AppendCsvRow(builder, fields);
        }

        writer.Write(builder.ToString());
    }

    private static void WriteMetadata(string outputPath, FuelDatasetOptions options, int missionCount, int architectureCount, int maxMissionAttempts)
    {
        var metadata = new
        {
            generator = "FuelDatasetGenerator",
            version = 5,
            createdUtc = DateTimeOffset.UtcNow,
            missionCount,
            architectureCount,
            options = new
            {
                options.OutputDirectory,
                options.MissionCount,
                options.Seed,
                options.StartYearUtc,
                options.EndYearUtc,
                options.MinPayloadMassKg,
                options.MaxPayloadMassKg,
                options.MinStageCount,
                options.MaxStageCount,
                options.MaxArchitecturesPerMissionOutput,
                maxMissionAttempts
            },
            assumptions = new
            {
                solarFluxAtOneAuWm2 = SolarFluxAtOneAu,
                departureStorageDays = "sampled from a short/nominal/extended mixture and redistributed across lower stages",
                arrivalStorageDays = "arrival or cruise stage stores propellant for the full transfer travel time",
                travelFactor = "sampled from fast/nominal/long-coast mixture with an outer-planet long-tail",
                correctionReserveDvMps = "clamp(total_dv * 0.03, 25, 300)",
                rocketEquation = "multi-stage, synthetic, chemical-only",
                stageHardwareModel = "dry mass includes tankage, engine dry mass, payload-relative structure overhead, and fixed service mass per stage",
                tankCarryProxy = "sum(stage_tank_mass_kg * stage_number) to expose upper-stage inert burden to the learner",
                note = "Representative engineering priors for ML dataset bootstrapping, not flight-certified performance."
            },
            stageTemplates = DepartureDvFractionsByStageCount
                .OrderBy(pair => pair.Key)
                .Select(pair => new
                {
                    totalStages = pair.Key + 1,
                    departureStageCount = pair.Key,
                    arrivalStageCount = 1,
                    departureDvFractions = pair.Value
                }),
            propellants = ChemicalPropellantCatalog.All.Select(propellant => new
            {
                propellant.Key,
                propellant.DisplayName,
                propellant.Oxidizer,
                propellant.Fuel,
                propellant.StorageClass,
                propellant.VacuumIspSeconds,
                propellant.MixtureDensityKgPerM3,
                propellant.BaseTankageFactor,
                propellant.StorageComplexityFactor,
                propellant.BaseBoiloffPerDayAt1Au,
                propellant.SolarFluxExponent,
                propellant.PrimaryBurnPenaltyFactor,
                propellant.LongCoastPenaltyFactor,
                propellant.RestartPenaltyFactor,
                propellant.HandlingPenaltyFactor
            }),
            engines = RocketEngineCatalog.All.Select(engine => new
            {
                engine.Key,
                engine.DisplayName,
                engine.PropellantKey,
                engine.VacuumIspSeconds,
                engine.VacuumThrustkN,
                engine.DryMassKg,
                engine.MaxClusterCount,
                engine.SelectionRank,
                engine.SupportsRestart,
                engine.SupportsLongCoast,
                engine.SupportedRoles,
                engine.SupportedSegments
            })
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json + Environment.NewLine);
    }

    private static string PickPlanet(Random rng, string? excludeName)
    {
        var candidates = DatasetPlanetNames
            .Where(name => !string.Equals(name, excludeName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var totalWeight = candidates.Sum(GetPlanetSamplingWeight);
        var roll = rng.NextDouble() * totalWeight;

        foreach (var candidate in candidates)
        {
            roll -= GetPlanetSamplingWeight(candidate);
            if (roll <= 0.0)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private static double GetPlanetSamplingWeight(string bodyName)
    {
        return bodyName switch
        {
            "Mercury" => 1.25,
            "Venus" => 1.10,
            "Earth" => 1.00,
            "Mars" => 1.00,
            "Jupiter" => 1.35,
            "Saturn" => 1.40,
            "Uranus" => 1.45,
            _ => 1.0
        };
    }

    private static bool IsOuterPlanet(string bodyName)
    {
        return bodyName is "Jupiter" or "Saturn" or "Uranus";
    }

    private static double ResolveMaxMissionDvMps(string originName, string destinationName)
    {
        return IsOuterPlanet(originName) || IsOuterPlanet(destinationName)
            ? OuterPlanetMaxMissionDvMps
            : DefaultMaxMissionDvMps;
    }

    private static double SampleParkingOrbitKm(OrbitalBody body, Random rng, bool isArrival)
    {
        if (!IsOuterPlanet(body.Name))
        {
            return isArrival
                ? 120.0 + rng.NextDouble() * 900.0
                : 150.0 + rng.NextDouble() * 650.0;
        }

        var radiusKm = body.Radius / 1000.0;
        var minMultiplier = isArrival ? 1.5 : 0.6;
        var maxMultiplier = isArrival ? 6.0 : 2.4;
        return radiusKm * (minMultiplier + rng.NextDouble() * (maxMultiplier - minMultiplier));
    }

    private static double SampleDepartureStorageDays(Random rng, string originName, string destinationName)
    {
        var outerMission = IsOuterPlanet(originName) || IsOuterPlanet(destinationName);
        var roll = rng.NextDouble();
        if (outerMission && roll < 0.45)
        {
            return 10.0 + rng.NextDouble() * 32.0;
        }

        if (roll < 0.20)
        {
            return 3.0 + rng.NextDouble() * 8.0;
        }

        if (roll < 0.80)
        {
            return 8.0 + rng.NextDouble() * 20.0;
        }

        return 18.0 + rng.NextDouble() * 24.0;
    }

    private static double SampleTravelFactor(Random rng, string originName, string destinationName)
    {
        var outerMission = IsOuterPlanet(originName) || IsOuterPlanet(destinationName);
        var roll = rng.NextDouble();
        if (roll < 0.16)
        {
            return 0.55 + rng.NextDouble() * 0.30;
        }

        if (roll < 0.72)
        {
            return 0.85 + rng.NextDouble() * 0.65;
        }

        return outerMission
            ? 1.45 + rng.NextDouble() * 1.35
            : 1.30 + rng.NextDouble() * 0.90;
    }

    private static bool? SampleLongWayOverride(Random rng)
    {
        var roll = rng.NextDouble();
        if (roll < 0.15)
        {
            return true;
        }

        if (roll < 0.55)
        {
            return false;
        }

        return null;
    }

    private static IReadOnlyList<StageProfile> BuildStageProfiles(MissionSample mission, int totalStageCount)
    {
        var departureStageCount = totalStageCount - 1;
        var departureFractions = DepartureDvFractionsByStageCount[departureStageCount];
        var profiles = new List<StageProfile>(totalStageCount);

        for (var departureIndex = 0; departureIndex < departureStageCount; departureIndex++)
        {
            var stageNumber = departureIndex + 1;
            profiles.Add(new StageProfile(
                stageNumber,
                totalStageCount,
                ResolveDepartureRole(departureStageCount, departureIndex),
                MissionSegment.Departure,
                mission.Transfer.DVEjection * departureFractions[departureIndex],
                EstimateDepartureStorageDays(mission.DepartureStorageDays, departureStageCount, departureIndex),
                mission.DepartureSolarFluxWm2 / SolarFluxAtOneAu,
                ResolveDepartureCandidates(departureStageCount, departureIndex)));
        }

        profiles.Add(new StageProfile(
            totalStageCount,
            totalStageCount,
            "cruise_arrival",
            MissionSegment.Arrival,
            mission.Transfer.DVInjection + mission.CorrectionReserveDvMps,
            mission.ArrivalStorageDays,
            mission.MeanSolarFluxWm2 / SolarFluxAtOneAu,
            CruiseArrivalStagePropellants));

        return profiles;
    }

    private static string ResolveDepartureRole(int departureStageCount, int departureIndex)
    {
        if (departureStageCount == 1)
        {
            return "departure_core";
        }

        if (departureIndex == 0)
        {
            return "booster";
        }

        if (departureIndex == departureStageCount - 1)
        {
            return "departure_upper";
        }

        return "departure_mid";
    }

    private static IReadOnlyList<ChemicalPropellant> ResolveDepartureCandidates(int departureStageCount, int departureIndex)
    {
        if (departureIndex == 0)
        {
            return FirstDepartureStagePropellants;
        }

        if (departureIndex == departureStageCount - 1)
        {
            return UpperDepartureStagePropellants;
        }

        return MiddleDepartureStagePropellants;
    }

    private static double EstimateDepartureStorageDays(double departureStorageDays, int departureStageCount, int departureIndex)
    {
        double[][] storageFractions =
        [
            [0.22],
            [0.08, 0.28],
            [0.05, 0.12, 0.24],
            [0.04, 0.08, 0.14, 0.22]
        ];

        var fractions = storageFractions[departureStageCount - 1];
        return Math.Max(0.2, departureStorageDays * fractions[departureIndex]);
    }

    private static double SampleLogUniform(Random rng, double min, double max)
    {
        var minLog = Math.Log(min);
        var maxLog = Math.Log(max);
        return Math.Exp(minLog + rng.NextDouble() * (maxLog - minLog));
    }

    private static int EstimateArchitecturesPerMission(FuelDatasetOptions options)
    {
        var count = 0;
        for (var totalStageCount = options.MinStageCount; totalStageCount <= options.MaxStageCount; totalStageCount++)
        {
            var departureStageCount = totalStageCount - 1;
            var combinations = CruiseArrivalStagePropellants.Count;
            for (var departureIndex = 0; departureIndex < departureStageCount; departureIndex++)
            {
                combinations *= ResolveDepartureCandidates(departureStageCount, departureIndex).Count;
            }

            count += combinations;
        }

        return count;
    }

    private static int EstimateRetainedArchitectureCapacity(FuelDatasetOptions options)
    {
        var totalPerMission = EstimateArchitecturesPerMission(options);
        var retainedPerMission = options.MaxArchitecturesPerMissionOutput > 0
            ? Math.Min(options.MaxArchitecturesPerMissionOutput, totalPerMission)
            : totalPerMission;
        return options.MissionCount * retainedPerMission;
    }

    private static int ResolveMaxMissionAttempts(FuelDatasetOptions options)
    {
        if (options.MaxMissionAttempts > 0)
        {
            return options.MaxMissionAttempts;
        }

        return Math.Max(10_000, options.MissionCount * 40);
    }

    private static string Format(double value)
    {
        return double.IsFinite(value)
            ? value.ToString("0.######", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static void AppendStageHeaders(ICollection<string> header, string prefix)
    {
        for (var stageNumber = 1; stageNumber <= MaxStageColumns; stageNumber++)
        {
            header.Add($"{prefix}_{stageNumber}_role");
            header.Add($"{prefix}_{stageNumber}_segment");
            header.Add($"{prefix}_{stageNumber}_propellant");
            header.Add($"{prefix}_{stageNumber}_engine_key");
            header.Add($"{prefix}_{stageNumber}_engine_count");
            header.Add($"{prefix}_{stageNumber}_engine_isp_seconds");
            header.Add($"{prefix}_{stageNumber}_engine_thrust_kn");
            header.Add($"{prefix}_{stageNumber}_total_engine_mass_kg");
            header.Add($"{prefix}_{stageNumber}_available_thrust_kn");
            header.Add($"{prefix}_{stageNumber}_dv_mps");
            header.Add($"{prefix}_{stageNumber}_storage_days");
            header.Add($"{prefix}_{stageNumber}_boiloff_fraction");
            header.Add($"{prefix}_{stageNumber}_loaded_propellant_kg");
            header.Add($"{prefix}_{stageNumber}_burn_propellant_kg");
            header.Add($"{prefix}_{stageNumber}_boiloff_mass_kg");
            header.Add($"{prefix}_{stageNumber}_tank_mass_kg");
            header.Add($"{prefix}_{stageNumber}_structure_mass_kg");
            header.Add($"{prefix}_{stageNumber}_dry_mass_kg");
            header.Add($"{prefix}_{stageNumber}_initial_mass_kg");
            header.Add($"{prefix}_{stageNumber}_penalty_kg");
            header.Add($"{prefix}_{stageNumber}_reason");
        }
    }

    private static void AppendStageFields(ICollection<string> fields, IReadOnlyList<StageMassEstimate> stages)
    {
        for (var stageNumber = 1; stageNumber <= MaxStageColumns; stageNumber++)
        {
            var stage = stages.FirstOrDefault(item => item.StageNumber == stageNumber);
            if (stage is null)
            {
                for (var i = 0; i < 21; i++)
                {
                    fields.Add(string.Empty);
                }

                continue;
            }

            fields.Add(stage.Role);
            fields.Add(stage.Segment.ToString());
            fields.Add(stage.Propellant.Key);
            fields.Add(stage.Engine?.Key ?? string.Empty);
            fields.Add(stage.EngineCount > 0 ? stage.EngineCount.ToString(CultureInfo.InvariantCulture) : string.Empty);
            fields.Add(Format(stage.EffectiveIspSeconds));
            fields.Add(stage.Engine is null ? string.Empty : Format(stage.Engine.VacuumThrustkN));
            fields.Add(Format(stage.TotalEngineMassKg));
            fields.Add(Format(stage.AvailableThrustkN));
            fields.Add(Format(stage.DeltaVMps));
            fields.Add(Format(stage.StorageDays));
            fields.Add(Format(stage.BoiloffFraction));
            fields.Add(Format(stage.LoadedPropellantMassKg));
            fields.Add(Format(stage.BurnPropellantMassKg));
            fields.Add(Format(stage.BoiloffMassKg));
            fields.Add(Format(stage.TankMassKg));
            fields.Add(Format(stage.StructureMassKg));
            fields.Add(Format(stage.DryMassKg));
            fields.Add(Format(stage.InitialMassKg));
            fields.Add(Format(stage.PenaltyMassKg));
            fields.Add(stage.InfeasibleReason);
        }
    }

    private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            AppendCsvField(builder, fields[i]);
        }

        builder.AppendLine();
    }

    private static void AppendCsvField(StringBuilder builder, string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        {
            builder.Append('"');
            builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            builder.Append('"');
            return;
        }

        builder.Append(value);
    }
}
