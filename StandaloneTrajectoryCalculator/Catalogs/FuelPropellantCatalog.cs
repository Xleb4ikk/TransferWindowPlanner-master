namespace StandaloneTrajectoryCalculator;

internal enum MissionSegment
{
    Departure,
    Arrival
}

internal sealed record ChemicalPropellant(
    string Key,
    string DisplayName,
    string Oxidizer,
    string Fuel,
    string StorageClass,
    double VacuumIspSeconds,
    double MixtureDensityKgPerM3,
    double BaseTankageFactor,
    double StorageComplexityFactor,
    double BaseBoiloffPerDayAt1Au,
    double SolarFluxExponent,
    double PrimaryBurnPenaltyFactor,
    double LongCoastPenaltyFactor,
    double RestartPenaltyFactor,
    double HandlingPenaltyFactor,
    bool IsCryogenic,
    bool IsHypergolic,
    bool IsLowThrust)
{
    public double EffectiveTankageFactor(double storageDays)
    {
        var normalizedStorage = Math.Sqrt(Math.Max(0.0, storageDays) / 30.0);
        return BaseTankageFactor + StorageComplexityFactor * normalizedStorage;
    }

    public double ComputeBoiloffFraction(double storageDays, double solarFluxRelative)
    {
        if (BaseBoiloffPerDayAt1Au <= 0 || storageDays <= 0)
        {
            return 0.0;
        }

        var relativeFlux = Math.Max(0.05, solarFluxRelative);
        var dailyRate = BaseBoiloffPerDayAt1Au * Math.Pow(relativeFlux, SolarFluxExponent);
        return 1.0 - Math.Exp(-dailyRate * storageDays);
    }
}

internal static class ChemicalPropellantCatalog
{
    // Representative values for a first synthetic training set.
    // They are intentionally conservative and should be treated as engineering priors,
    // not flight-certified stage-performance guarantees.
    public static IReadOnlyList<ChemicalPropellant> All { get; } =
    [
        new(
            Key: "lox_lh2",
            DisplayName: "LOX/LH2",
            Oxidizer: "LOX",
            Fuel: "LH2",
            StorageClass: "cryogenic",
            VacuumIspSeconds: 450.0,
            MixtureDensityKgPerM3: 360.0,
            BaseTankageFactor: 0.16,
            StorageComplexityFactor: 0.035,
            BaseBoiloffPerDayAt1Au: 0.0035,
            SolarFluxExponent: 1.10,
            PrimaryBurnPenaltyFactor: 0.00,
            LongCoastPenaltyFactor: 0.18,
            RestartPenaltyFactor: 0.01,
            HandlingPenaltyFactor: 0.02,
            IsCryogenic: true,
            IsHypergolic: false,
            IsLowThrust: false),
        new(
            Key: "lox_ch4",
            DisplayName: "LOX/LCH4",
            Oxidizer: "LOX",
            Fuel: "LCH4",
            StorageClass: "semi-cryogenic",
            VacuumIspSeconds: 360.0,
            MixtureDensityKgPerM3: 830.0,
            BaseTankageFactor: 0.10,
            StorageComplexityFactor: 0.020,
            BaseBoiloffPerDayAt1Au: 0.0010,
            SolarFluxExponent: 1.00,
            PrimaryBurnPenaltyFactor: 0.01,
            LongCoastPenaltyFactor: 0.07,
            RestartPenaltyFactor: 0.02,
            HandlingPenaltyFactor: 0.01,
            IsCryogenic: true,
            IsHypergolic: false,
            IsLowThrust: false),
        new(
            Key: "lox_rp1",
            DisplayName: "LOX/RP-1",
            Oxidizer: "LOX",
            Fuel: "RP-1",
            StorageClass: "semi-cryogenic",
            VacuumIspSeconds: 330.0,
            MixtureDensityKgPerM3: 1020.0,
            BaseTankageFactor: 0.07,
            StorageComplexityFactor: 0.012,
            BaseBoiloffPerDayAt1Au: 0.00035,
            SolarFluxExponent: 0.95,
            PrimaryBurnPenaltyFactor: 0.00,
            LongCoastPenaltyFactor: 0.12,
            RestartPenaltyFactor: 0.06,
            HandlingPenaltyFactor: 0.01,
            IsCryogenic: true,
            IsHypergolic: false,
            IsLowThrust: false),
        new(
            Key: "nto_mmh",
            DisplayName: "NTO/MMH",
            Oxidizer: "NTO",
            Fuel: "MMH",
            StorageClass: "storable",
            VacuumIspSeconds: 322.0,
            MixtureDensityKgPerM3: 1150.0,
            BaseTankageFactor: 0.09,
            StorageComplexityFactor: 0.004,
            BaseBoiloffPerDayAt1Au: 0.00002,
            SolarFluxExponent: 0.15,
            PrimaryBurnPenaltyFactor: 0.04,
            LongCoastPenaltyFactor: 0.00,
            RestartPenaltyFactor: 0.00,
            HandlingPenaltyFactor: 0.03,
            IsCryogenic: false,
            IsHypergolic: true,
            IsLowThrust: false),
        new(
            Key: "hydrazine",
            DisplayName: "Hydrazine",
            Oxidizer: "none",
            Fuel: "N2H4",
            StorageClass: "storable",
            VacuumIspSeconds: 230.0,
            MixtureDensityKgPerM3: 1010.0,
            BaseTankageFactor: 0.12,
            StorageComplexityFactor: 0.005,
            BaseBoiloffPerDayAt1Au: 0.00001,
            SolarFluxExponent: 0.10,
            PrimaryBurnPenaltyFactor: 0.20,
            LongCoastPenaltyFactor: 0.01,
            RestartPenaltyFactor: 0.00,
            HandlingPenaltyFactor: 0.04,
            IsCryogenic: false,
            IsHypergolic: false,
            IsLowThrust: true)
    ];

    public static IReadOnlyDictionary<string, ChemicalPropellant> ByKey { get; } =
        All.ToDictionary(propellant => propellant.Key, StringComparer.OrdinalIgnoreCase);

    public static ChemicalPropellant Get(string key)
    {
        if (!ByKey.TryGetValue(key, out var propellant))
        {
            throw new InvalidOperationException($"Unknown propellant key '{key}'.");
        }

        return propellant;
    }
}
