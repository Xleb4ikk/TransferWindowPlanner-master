using System.Globalization;
using TrajectoryCalculator;

// ============================================================
// Через SolarSystemCatalog (встроенные планеты)
// ============================================================
static ScenarioInput CreateScenarioWithPresets()
{
    return new ScenarioInput
    {
        Calendar = SolarSystemCatalog.CreateUtcCalendarInput(),
        CentralBody = SolarSystemCatalog.CreateSunInput(),
        Bodies = [.. SolarSystemCatalog.CreateSelectedBodies("Earth", "Mars")],
        Request = new CalculationRequest
        {
            Mode = "single",
            Origin = "Earth",
            Destination = "Mars",
            DepartureTime = SolarSystemCatalog.ToJ2000Seconds(
                new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)),
            TravelTime = 220 * 86400.0,       // 220 дней в секундах
            DepartureParkingOrbitAltitude = 200_000,    // 200 км
            ArrivalParkingOrbitAltitude = 250_000,      // 250 км
            ArrivalManeuverMode = "circular-capture",
        }
    };
}

// ============================================================
// 2. Полный пример — ВСЕ возможные параметры
// ============================================================
static ScenarioInput CreateFullScenario()
{
    return new ScenarioInput
    {
        // --- КАЛЕНДАРЬ ---
        Calendar = new CalendarInput
        {
            Kind = "utc",                        // "utc" | "custom"
            EpochUtc = "2000-01-01T12:00:00Z",  // точка отсчёта (J2000)
            EpochYear = 2000,                    // год эпохи
            EpochDayOfYear = 1,                  // день эпохи
            DaysPerYear = 365,                   // дней в году
            HoursPerDay = 24,                    // часов в сутках
            MinutesPerHour = 60,                 // минут в часе
            SecondsPerMinute = 60                // секунд в минуте
        },

        // --- ЦЕНТРАЛЬНОЕ ТЕЛО (Солнце) ---
        CentralBody = new CentralBodyInput
        {
            Name = "Sun",
            GravitationalParameter = 1.3271244004127942E+20  // м³/с²
        },

        // --- НЕБЕСНЫЕ ТЕЛА ---
        Bodies =
        [
            // === ЗЕМЛЯ ===
            new BodyInput
            {
                Name = "Earth",
                GravitationalParameter = 3.98600435507E+14,    // м³/с²
                Radius = 6_371_008.4,                          // м
                SphereOfInfluence = 924_646_955.636956,         // м (сфера Хилла)
                RotationPeriodSeconds = 86_164.0905,            // период вращения (с)
                Orbit = new OrbitInput
                {
                    // Кеплеровы элементы (эпоха J2000)
                    SemiMajorAxis = 149_597_897_627.61673,         // м (1 а.е.)
                    Eccentricity = 0.01673163,
                    InclinationDeg = -0.00054346,
                    LongitudeOfAscendingNodeDeg = -5.11260389,
                    ArgumentOfPeriapsisDeg = 108.04266274,
                    MeanAnomalyAtEpochDeg = 357.53685687,
                    Epoch = 0,                                     // J2000

                    // Вековые изменения (опционально)
                    SemiMajorAxisRate = -4487.936121,                    // м/век
                    EccentricityRate = -3.661E-05,
                    InclinationRateDegPerCentury = -0.01337178,
                    LongitudeOfAscendingNodeRateDegPerCentury = -0.24123856,
                    LongitudeOfPeriapsisDeg = 102.93005885,
                    LongitudeOfPeriapsisRateDegPerCentury = 0.3179526,
                    MeanLongitudeDeg = 100.46691572,
                    MeanLongitudeRateDegPerCentury = 35999.37306329,

                    // Члены высших порядков (опционально, для газовых гигантов)
                    MeanAnomalyQuadraticTermDegPerCentury2 = 0,
                    MeanAnomalyCosineTermDeg = 0,
                    MeanAnomalySineTermDeg = 0,
                    MeanAnomalyFrequencyDegPerCentury = 0
                }
            },

            // === МАРС ===
            new BodyInput
            {
                Name = "Mars",
                GravitationalParameter = 4.282837362E+13,
                Radius = 3_389_500.0,
                SphereOfInfluence = 577_239_978.521152,
                RotationPeriodSeconds = 88_642.6848,
                Orbit = new OrbitInput
                {
                    SemiMajorAxis = 227_944_135_087.1228,
                    Eccentricity = 0.09336511,
                    InclinationDeg = 1.85181869,
                    LongitudeOfAscendingNodeDeg = 49.71320984,
                    ArgumentOfPeriapsisDeg = 286.36934232,
                    MeanAnomalyAtEpochDeg = 19.3493162,
                    Epoch = 0,
                    SemiMajorAxisRate = 145_109.934579,
                    EccentricityRate = 9.149E-05,
                    InclinationRateDegPerCentury = -0.00724757,
                    LongitudeOfAscendingNodeRateDegPerCentury = -0.26852431,
                    LongitudeOfPeriapsisDeg = -23.91744784,
                    LongitudeOfPeriapsisRateDegPerCentury = 0.45223625,
                    MeanLongitudeDeg = -4.56813164,
                    MeanLongitudeRateDegPerCentury = 19140.29934243
                }
            }
        ],

        // --- ЗАПРОС НА РАСЧЁТ ---
        Request = new CalculationRequest
        {
            // Режим: "single" — один перелёт, "porkchop" — сканирование сетки
            Mode = "single",

            Origin = "Earth",
            Destination = "Mars",

            // Для single-режима:
            DepartureTime = SolarSystemCatalog.ToJ2000Seconds(
                new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)),
            TravelTime = 220 * 86400.0,   // 220 суток в секундах

            // Для porkchop-режима (раскомментировать и переключить Mode):
            // DepartureWindowStart = SolarSystemCatalog.ToJ2000Seconds(
            //     new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            // DepartureWindowEnd = SolarSystemCatalog.ToJ2000Seconds(
            //     new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            // TravelTimeMin = 120 * 86400.0,
            // TravelTimeMax = 360 * 86400.0,
            // DepartureSteps = 48,       // шагов по дате старта
            // TravelTimeSteps = 48,       // шагов по времени полёта

            // Параметры орбиты отлёта
            DepartureParkingOrbitAltitude = 200_000,                // опорная орбита (м)
            DepartureParkingOrbitPeriapsisAltitude = 200_000,       // перицентр опорной (м)
            DepartureParkingOrbitApoapsisAltitude = 200_000,        // апоцентр опорной (м)

            // Параметры прибытия
            ArrivalParkingOrbitAltitude = 250_000,                  // целевая орбита (м), null = не считать
            ArrivalManeuverMode = "circular-capture",               // circular-capture | elliptic-capture | aerobraking | flyby | ignore-arrival-burn
            UseAerobraking = false,

            // Если true — перелёт "дальней стороной" (угол > 180°)
            LongWay = false,

            // Куда сохранить результат
            CsvOutputPath = "data/results/porkchop.csv",           // только для porkchop
            ResultOutputPath = "data/results/result.json",

            // --- РАСЧЁТ ПОДЪЁМА С ПОВЕРХНОСТИ (опционально) ---
            Launch = new LaunchConfiguration
            {
                Enabled = true,                    // включить расчёт
                Mode = "quick",                    // "quick" | "detailed"
                RocketInitialMass = 500_000,       // начальная масса (кг)
                RocketThrust = 7_000_000,          // тяга (Н)
                RocketIsp = 310,                   // удельный импульс (с)
                LaunchLatitude = 28.5,             // широта старта (градусы)
                LaunchLongitude = 0.0,             // долгота старта (градусы, на эпоху J2000)
                TargetInclination = 0.0            // целевое наклонение (градусы)
            }
        }
    };
}

// ============================================================
// 3. Porkchop — сканирование окна пуска
// ============================================================
static ScenarioInput CreatePorkchopScenario()
{
    return new ScenarioInput
    {
        Calendar = SolarSystemCatalog.CreateUtcCalendarInput(),
        CentralBody = SolarSystemCatalog.CreateSunInput(),
        Bodies = [.. SolarSystemCatalog.CreateSelectedBodies("Earth", "Mars")],
        Request = new CalculationRequest
        {
            Mode = "porkchop",
            Origin = "Earth",
            Destination = "Mars",

            // Окно поиска
            DepartureWindowStart = SolarSystemCatalog.ToJ2000Seconds(
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)),
            DepartureWindowEnd = SolarSystemCatalog.ToJ2000Seconds(
                new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)),
            TravelTimeMin = 120 * 86400.0,
            TravelTimeMax = 360 * 86400.0,

            // Разрешение сетки
            DepartureSteps = 48,
            TravelTimeSteps = 48,

            DepartureParkingOrbitAltitude = 200_000,
            ArrivalParkingOrbitAltitude = 250_000,
            ArrivalManeuverMode = "circular-capture",
        }
    };
}

// ============================================================
// ТОЧКА ВХОДА
// ============================================================
Console.OutputEncoding = System.Text.Encoding.UTF8;
var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
culture.NumberFormat.NumberDecimalSeparator = ".";
CultureInfo.CurrentCulture = culture;
CultureInfo.CurrentUICulture = culture;

Console.WriteLine("=== TransferWindowPlanner — Full API Example ===");
Console.WriteLine();

// ---- Демонстрация 1: простой сценарий через SolarSystemCatalog ----
Console.WriteLine(">>> 1. Single transfer (Earth -> Mars) через SolarSystemCatalog");
Console.WriteLine(new string('-', 60));

var planner = new TrajectoryPlanner();
var simpleInput = CreateScenarioWithPresets();
var result = planner.Calculate(simpleInput);

Console.WriteLine(result.ConsoleSummary);
Console.WriteLine();

// Доступ ко всем полям результата:
if (result.Transfer is not null)
{
    Console.WriteLine($"  Delta-V в деталях:");
    Console.WriteLine($"    Ejection dV:  {result.Transfer.DVEjection:0.0} м/с");
    Console.WriteLine($"    Injection dV: {result.Transfer.DVInjection:0.0} м/с");
    Console.WriteLine($"    Total dV:     {result.Transfer.DVTotal:0.0} м/с");
    Console.WriteLine($"    Phase angle:  {result.Transfer.PhaseAngle * (180.0 / Math.PI):0.00} deg");
    Console.WriteLine($"    Ejection inc: {result.Transfer.EjectionInclination * (180.0 / Math.PI):0.00} deg");
    Console.WriteLine($"    Insertion inc:{result.Transfer.InsertionInclination * (180.0 / Math.PI):0.00} deg");
    Console.WriteLine();
}

if (result.Launch is not null)
{
    Console.WriteLine($"  Launch analysis:");
    Console.WriteLine($"    Ideal dV:     {result.Launch.IdealDeltaVMps:0.0} м/с");
    Console.WriteLine($"    Required dV:  {result.Launch.RequiredDeltaVMps:0.0} м/с");
    Console.WriteLine($"    Successful:   {result.Launch.Successful}");
    Console.WriteLine();
}

// ---- Демонстрация 2: Porkchop ----
Console.WriteLine(">>> 2. Porkchop scan (Earth -> Mars)");
Console.WriteLine(new string('-', 60));

var porkchopInput = CreatePorkchopScenario();
var porkchopResult = planner.Calculate(porkchopInput);

Console.WriteLine(porkchopResult.ConsoleSummary);
Console.WriteLine();

if (porkchopResult.Porkchop is not null)
{
    Console.WriteLine($"  Сетка: {porkchopResult.Porkchop.Window.DepartureSteps}x{porkchopResult.Porkchop.Window.TravelTimeSteps}");
    Console.WriteLine($"  Валидных точек: {porkchopResult.Porkchop.ValidPoints}");
    Console.WriteLine($"  Синодический период: {porkchopResult.Porkchop.SynodicPeriod / 86400.0:0.0} дней");
    Console.WriteLine($"  Hohmann TOF: {porkchopResult.Porkchop.HohmannTimeOfFlight / 86400.0:0.0} дней");
    Console.WriteLine();
}

// ---- Демонстрация 3: Полный сценарий со всеми параметрами ----
Console.WriteLine(">>> 3. Полный сценарий (все параметры)");
Console.WriteLine(new string('-', 60));

var fullInput = CreateFullScenario();

// Сериализация в JSON (чтобы увидеть все поля)
var json = ScenarioJson.Serialize(fullInput);
Console.WriteLine("Сгенерированный JSON:");
Console.WriteLine(json);
Console.WriteLine();

// ---- Демонстрация 4: Загрузка из JSON-файла ----
Console.WriteLine(">>> 4. Загрузка сценария из JSON-файла");
Console.WriteLine(new string('-', 60));

var solutionDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var sampleFile = Path.Combine(solutionDir, "StandaloneTrajectoryCalculator", "sample-single.json");
var loaded = ScenarioJson.LoadFromFile(sampleFile);

if (loaded is not null)
{
    var loadedResult = planner.Calculate(loaded);
    Console.WriteLine(loadedResult.ConsoleSummary);
}
else
{
    Console.WriteLine("Файл sample-single.json не найден.");
}

Console.WriteLine();
Console.WriteLine("=== Готово ===");
