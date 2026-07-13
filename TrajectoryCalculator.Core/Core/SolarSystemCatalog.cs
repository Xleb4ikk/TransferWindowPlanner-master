using System.Globalization;

namespace TrajectoryCalculator;

/// <summary>
/// JPL-based Solar System ephemeris catalog providing preset planetary data.
/// </summary>
public static class SolarSystemCatalog
{
    public const string CatalogId = "solar-system-jpl";
    public const double AstronomicalUnit = 149_597_870_700.0;
    public const double SecondsPerDay = 86_400.0;
    public const double DaysPerJulianCentury = 36_525.0;
    public const double SecondsPerJulianCentury = DaysPerJulianCentury * SecondsPerDay;
    public const double SunGravitationalParameter = 1.3271244004127942e20;
    public static readonly DateTimeOffset J2000Utc = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly PlanetPreset[] Presets =
    [
        new(
            "Mercury",
            2.2031868551e13,
            2_439_400.0,
            5_067_030.0,
            new OrbitalSeries(
                0.38709843, 0.00000000,
                0.20563661, 0.00002123,
                7.00559432, -0.00590158,
                48.33961819, -0.12214182,
                77.45771895, 0.15940013,
                252.25166724, 149472.67486623)),
        new(
            "Venus",
            3.24858592e14,
            6_051_800.0,
            -20_997_360.0,
            new OrbitalSeries(
                0.72332102, -0.00000026,
                0.00676399, -0.00005107,
                3.39777545, 0.00043494,
                76.67261496, -0.27274174,
                131.76755713, 0.05679648,
                181.97970850, 58517.81560260)),
        new(
            "Earth",
            3.98600435507e14,
            6_371_008.4,
            86_164.0905,
            new OrbitalSeries(
                1.00000018, -0.00000003,
                0.01673163, -0.00003661,
                -0.00054346, -0.01337178,
                -5.11260389, -0.24123856,
                102.93005885, 0.31795260,
                100.46691572, 35999.37306329)),
        new(
            "Mars",
            4.282837362e13,
            3_389_500.0,
            88_642.6848,
            new OrbitalSeries(
                1.52371243, 0.00000097,
                0.09336511, 0.00009149,
                1.85181869, -0.00724757,
                49.71320984, -0.26852431,
                -23.91744784, 0.45223625,
                -4.56813164, 19140.29934243)),
        new(
            "Jupiter",
            1.266865319e17,
            69_911_000.0,
            35_730.0,
            new OrbitalSeries(
                5.20248019, -0.00002864,
                0.04853590, 0.00018026,
                1.29861416, -0.00322699,
                100.29282654, 0.13024619,
                14.27495244, 0.18199196,
                34.33479152, 3034.90371757,
                -0.00012452, 0.06064060, -0.35635438, 38.35125000)),
        new(
            "Saturn",
            3.793120623e16,
            58_232_000.0,
            38_362.0,
            new OrbitalSeries(
                9.54149883, -0.00003065,
                0.05550825, -0.00032044,
                2.49424102, 0.00451969,
                113.63998702, -0.25015002,
                92.86136063, 0.54179478,
                50.07571329, 1222.11494724,
                0.00025899, -0.13434469, 0.87320147, 38.35125000)),
        new(
            "Uranus",
            5.7939513e15,
            25_362_000.0,
            -62_064.0,
            new OrbitalSeries(
                19.18797948, -0.00020455,
                0.04685740, -0.00001550,
                0.77298127, -0.00180155,
                73.96250215, 0.05739699,
                172.43404441, 0.09266985,
                314.20276625, 428.49512595,
                0.00058331, -0.97731848, 0.17689245, 7.67025000)),
        new(
            "Neptune",
            6.83509997e15,
            24_622_000.0,
            57_996.0,
            new OrbitalSeries(
                30.06952752, 0.00006447,
                0.00895439, 0.00000818,
                1.77005520, 0.00022400,
                131.78635853, -0.00606302,
                46.68158724, 0.01009938,
                304.22289287, 218.46515314,
                -0.00041348, 0.68346318, -0.10162547, 7.67025000))
    ];

    private static readonly IReadOnlyDictionary<string, PlanetPreset> PresetsByName =
        Presets.ToDictionary(preset => preset.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> PlanetNames { get; } = Presets.Select(preset => preset.Name).ToArray();

    /// <summary>
    /// Creates a UTC-based calendar input preset.
    /// </summary>
    public static CalendarInput CreateUtcCalendarInput()
    {
        return new CalendarInput
        {
            Kind = "utc",
            EpochUtc = FormatUtc(J2000Utc),
            EpochYear = 2000,
            EpochDayOfYear = 1,
            DaysPerYear = 365,
            HoursPerDay = 24,
            MinutesPerHour = 60,
            SecondsPerMinute = 60
        };
    }

    /// <summary>
    /// Creates a Sun central body input preset.
    /// </summary>
    public static CentralBodyInput CreateSunInput()
    {
        return new CentralBodyInput
        {
            Name = "Sun",
            GravitationalParameter = SunGravitationalParameter
        };
    }

    /// <summary>
    /// Creates a body input for a named planet from the JPL catalog.
    /// </summary>
    public static BodyInput CreateBodyInput(string planetName)
    {
        if (!PresetsByName.TryGetValue(planetName, out var preset))
        {
            throw new InvalidOperationException($"Unknown Solar System planet '{planetName}'.");
        }

        return preset.ToBodyInput();
    }

    public static OrbitalBody CreateOrbitalBody(string planetName)
    {
        return CreateOrbitalBody(CreateBodyInput(planetName));
    }

    public static IReadOnlyList<OrbitalBody> CreateOrbitalBodies()
    {
        return PlanetNames.Select(CreateOrbitalBody).ToArray();
    }

    public static IReadOnlyList<BodyInput> CreateSelectedBodies(params string[] planetNames)
    {
        var names = planetNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return names.Select(CreateBodyInput).ToArray();
    }

    public static ScenarioInput CreateSingleTransferTemplate()
    {
        return new ScenarioInput
        {
            Calendar = CreateUtcCalendarInput(),
            CentralBody = CreateSunInput(),
            Bodies =
            [
                CreateBodyInput("Earth"),
                CreateBodyInput("Mars")
            ],
            Request = new CalculationRequest
            {
                Mode = "single",
                Origin = "Earth",
                Destination = "Mars",
                DepartureTime = ToJ2000Seconds(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)),
                TravelTime = 220 * SecondsPerDay,
                DepartureParkingOrbitAltitude = 200_000.0,
                DepartureParkingOrbitPeriapsisAltitude = 200_000.0,
                DepartureParkingOrbitApoapsisAltitude = 200_000.0,
                ArrivalParkingOrbitAltitude = 250_000.0,
                ArrivalManeuverMode = "circular-capture",
                ResultOutputPath = "data/results/earth-mars-single.json"
            }
        };
    }

    public static ScenarioInput CreatePorkchopTemplate()
    {
        return new ScenarioInput
        {
            Calendar = CreateUtcCalendarInput(),
            CentralBody = CreateSunInput(),
            Bodies =
            [
                CreateBodyInput("Earth"),
                CreateBodyInput("Mars")
            ],
            Request = new CalculationRequest
            {
                Mode = "porkchop",
                Origin = "Earth",
                Destination = "Mars",
                DepartureWindowStart = ToJ2000Seconds(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
                DepartureWindowEnd = ToJ2000Seconds(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)),
                TravelTimeMin = 120 * SecondsPerDay,
                TravelTimeMax = 360 * SecondsPerDay,
                DepartureSteps = 48,
                TravelTimeSteps = 48,
                DepartureParkingOrbitAltitude = 200_000.0,
                DepartureParkingOrbitPeriapsisAltitude = 200_000.0,
                DepartureParkingOrbitApoapsisAltitude = 200_000.0,
                ArrivalParkingOrbitAltitude = 250_000.0,
                ArrivalManeuverMode = "circular-capture",
                CsvOutputPath = "data/results/earth-mars-porkchop.csv",
                ResultOutputPath = "data/results/earth-mars-porkchop.json"
            }
        };
    }

    /// <summary>
    /// Converts a UTC DateTimeOffset to J2000 seconds.
    /// </summary>
    public static double ToJ2000Seconds(DateTimeOffset utcDateTime)
    {
        return (utcDateTime.ToUniversalTime() - J2000Utc).TotalSeconds;
    }

    public static DateTimeOffset FromJ2000Seconds(double secondsSinceJ2000)
    {
        return J2000Utc.AddSeconds(secondsSinceJ2000);
    }

    public static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static double NormalizeDegrees(double angle)
    {
        angle %= 360.0;
        if (angle < 0)
        {
            angle += 360.0;
        }

        return angle;
    }

    private static OrbitalBody CreateOrbitalBody(BodyInput body)
    {
        var argumentOfPeriapsisDeg = ResolveArgumentOfPeriapsisDegrees(body.Orbit);
        var meanAnomalyAtEpochDeg = ResolveMeanAnomalyAtEpochDegrees(body.Orbit);
        var secularTerms = BuildSecularTerms(body.Orbit);

        var orbit = new OrbitalElements(
            body.Orbit.SemiMajorAxis,
            body.Orbit.Eccentricity,
            body.Orbit.InclinationDeg * LambertSolver.Deg2Rad,
            body.Orbit.LongitudeOfAscendingNodeDeg * LambertSolver.Deg2Rad,
            argumentOfPeriapsisDeg * LambertSolver.Deg2Rad,
            meanAnomalyAtEpochDeg * LambertSolver.Deg2Rad,
            body.Orbit.Epoch,
            SunGravitationalParameter,
            secularTerms);

        return new OrbitalBody(
            body.Name,
            body.GravitationalParameter,
            body.Radius,
            body.SphereOfInfluence,
            orbit,
            body.RotationPeriodSeconds);
    }

    private static double ResolveArgumentOfPeriapsisDegrees(OrbitInput orbit)
    {
        if (orbit.LongitudeOfPeriapsisDeg.HasValue)
        {
            return NormalizeDegrees(orbit.LongitudeOfPeriapsisDeg.Value - orbit.LongitudeOfAscendingNodeDeg);
        }

        return orbit.ArgumentOfPeriapsisDeg;
    }

    private static double ResolveMeanAnomalyAtEpochDegrees(OrbitInput orbit)
    {
        if (orbit.MeanLongitudeDeg.HasValue && orbit.LongitudeOfPeriapsisDeg.HasValue)
        {
            var correction = orbit.MeanAnomalyCosineTermDeg ?? 0.0;
            return NormalizeDegrees(orbit.MeanLongitudeDeg.Value - orbit.LongitudeOfPeriapsisDeg.Value + correction);
        }

        return orbit.MeanAnomalyAtEpochDeg;
    }

    private static SecularOrbitTerms? BuildSecularTerms(OrbitInput orbit)
    {
        if (!orbit.MeanLongitudeDeg.HasValue && !orbit.LongitudeOfPeriapsisDeg.HasValue)
        {
            return null;
        }

        return new SecularOrbitTerms(
            orbit.SemiMajorAxisRate ?? 0.0,
            orbit.EccentricityRate ?? 0.0,
            (orbit.InclinationRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.LongitudeOfAscendingNodeRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            orbit.LongitudeOfPeriapsisDeg.GetValueOrDefault() * LambertSolver.Deg2Rad,
            (orbit.LongitudeOfPeriapsisRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            orbit.MeanLongitudeDeg.GetValueOrDefault() * LambertSolver.Deg2Rad,
            (orbit.MeanLongitudeRateDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalyQuadraticTermDegPerCentury2 ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalyCosineTermDeg ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalySineTermDeg ?? 0.0) * LambertSolver.Deg2Rad,
            (orbit.MeanAnomalyFrequencyDegPerCentury ?? 0.0) * LambertSolver.Deg2Rad);
    }

    private sealed class PlanetPreset
    {
        public PlanetPreset(string name, double gravitationalParameter, double meanRadiusMeters, double rotationPeriodSeconds, OrbitalSeries orbitalSeries)
        {
            Name = name;
            GravitationalParameter = gravitationalParameter;
            MeanRadiusMeters = meanRadiusMeters;
            RotationPeriodSeconds = rotationPeriodSeconds;
            OrbitalSeries = orbitalSeries;
        }

        public string Name { get; }
        public double GravitationalParameter { get; }
        public double MeanRadiusMeters { get; }
        public double RotationPeriodSeconds { get; }
        public OrbitalSeries OrbitalSeries { get; }

        public BodyInput ToBodyInput()
        {
            var semiMajorAxisAtEpoch = OrbitalSeries.SemiMajorAxisAu * AstronomicalUnit;
            var meanAnomalyAtEpoch = NormalizeDegrees(
                OrbitalSeries.MeanLongitudeDeg -
                OrbitalSeries.LongitudeOfPeriapsisDeg +
                OrbitalSeries.MeanAnomalyCosineTermDeg);

            return new BodyInput
            {
                Name = Name,
                GravitationalParameter = GravitationalParameter,
                Radius = MeanRadiusMeters,
                SphereOfInfluence = semiMajorAxisAtEpoch * Math.Pow(GravitationalParameter / SunGravitationalParameter, 2.0 / 5.0),
                RotationPeriodSeconds = RotationPeriodSeconds,
                Orbit = new OrbitInput
                {
                    SemiMajorAxis = semiMajorAxisAtEpoch,
                    Eccentricity = OrbitalSeries.Eccentricity,
                    InclinationDeg = OrbitalSeries.InclinationDeg,
                    LongitudeOfAscendingNodeDeg = OrbitalSeries.LongitudeOfAscendingNodeDeg,
                    ArgumentOfPeriapsisDeg = NormalizeDegrees(
                        OrbitalSeries.LongitudeOfPeriapsisDeg - OrbitalSeries.LongitudeOfAscendingNodeDeg),
                    MeanAnomalyAtEpochDeg = meanAnomalyAtEpoch,
                    Epoch = 0.0,
                    SemiMajorAxisRate = OrbitalSeries.SemiMajorAxisRateAuPerCentury * AstronomicalUnit,
                    EccentricityRate = OrbitalSeries.EccentricityRatePerCentury,
                    InclinationRateDegPerCentury = OrbitalSeries.InclinationRateDegPerCentury,
                    LongitudeOfAscendingNodeRateDegPerCentury = OrbitalSeries.LongitudeOfAscendingNodeRateDegPerCentury,
                    LongitudeOfPeriapsisDeg = OrbitalSeries.LongitudeOfPeriapsisDeg,
                    LongitudeOfPeriapsisRateDegPerCentury = OrbitalSeries.LongitudeOfPeriapsisRateDegPerCentury,
                    MeanLongitudeDeg = OrbitalSeries.MeanLongitudeDeg,
                    MeanLongitudeRateDegPerCentury = OrbitalSeries.MeanLongitudeRateDegPerCentury,
                    MeanAnomalyQuadraticTermDegPerCentury2 = OrbitalSeries.MeanAnomalyQuadraticTermDegPerCentury2,
                    MeanAnomalyCosineTermDeg = OrbitalSeries.MeanAnomalyCosineTermDeg,
                    MeanAnomalySineTermDeg = OrbitalSeries.MeanAnomalySineTermDeg,
                    MeanAnomalyFrequencyDegPerCentury = OrbitalSeries.MeanAnomalyFrequencyDegPerCentury
                }
            };
        }
    }

    private sealed class OrbitalSeries
    {
        public OrbitalSeries(
            double semiMajorAxisAu,
            double semiMajorAxisRateAuPerCentury,
            double eccentricity,
            double eccentricityRatePerCentury,
            double inclinationDeg,
            double inclinationRateDegPerCentury,
            double longitudeOfAscendingNodeDeg,
            double longitudeOfAscendingNodeRateDegPerCentury,
            double longitudeOfPeriapsisDeg,
            double longitudeOfPeriapsisRateDegPerCentury,
            double meanLongitudeDeg,
            double meanLongitudeRateDegPerCentury,
            double meanAnomalyQuadraticTermDegPerCentury2 = 0.0,
            double meanAnomalyCosineTermDeg = 0.0,
            double meanAnomalySineTermDeg = 0.0,
            double meanAnomalyFrequencyDegPerCentury = 0.0)
        {
            SemiMajorAxisAu = semiMajorAxisAu;
            SemiMajorAxisRateAuPerCentury = semiMajorAxisRateAuPerCentury;
            Eccentricity = eccentricity;
            EccentricityRatePerCentury = eccentricityRatePerCentury;
            InclinationDeg = inclinationDeg;
            InclinationRateDegPerCentury = inclinationRateDegPerCentury;
            LongitudeOfAscendingNodeDeg = longitudeOfAscendingNodeDeg;
            LongitudeOfAscendingNodeRateDegPerCentury = longitudeOfAscendingNodeRateDegPerCentury;
            LongitudeOfPeriapsisDeg = longitudeOfPeriapsisDeg;
            LongitudeOfPeriapsisRateDegPerCentury = longitudeOfPeriapsisRateDegPerCentury;
            MeanLongitudeDeg = meanLongitudeDeg;
            MeanLongitudeRateDegPerCentury = meanLongitudeRateDegPerCentury;
            MeanAnomalyQuadraticTermDegPerCentury2 = meanAnomalyQuadraticTermDegPerCentury2;
            MeanAnomalyCosineTermDeg = meanAnomalyCosineTermDeg;
            MeanAnomalySineTermDeg = meanAnomalySineTermDeg;
            MeanAnomalyFrequencyDegPerCentury = meanAnomalyFrequencyDegPerCentury;
        }

        public double SemiMajorAxisAu { get; }
        public double SemiMajorAxisRateAuPerCentury { get; }
        public double Eccentricity { get; }
        public double EccentricityRatePerCentury { get; }
        public double InclinationDeg { get; }
        public double InclinationRateDegPerCentury { get; }
        public double LongitudeOfAscendingNodeDeg { get; }
        public double LongitudeOfAscendingNodeRateDegPerCentury { get; }
        public double LongitudeOfPeriapsisDeg { get; }
        public double LongitudeOfPeriapsisRateDegPerCentury { get; }
        public double MeanLongitudeDeg { get; }
        public double MeanLongitudeRateDegPerCentury { get; }
        public double MeanAnomalyQuadraticTermDegPerCentury2 { get; }
        public double MeanAnomalyCosineTermDeg { get; }
        public double MeanAnomalySineTermDeg { get; }
        public double MeanAnomalyFrequencyDegPerCentury { get; }
    }
}
