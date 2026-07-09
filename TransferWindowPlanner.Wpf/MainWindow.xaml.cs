using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Core = StandaloneTrajectoryCalculator;

namespace TransferWindowPlanner.Wpf;

using Gui = StandaloneTrajectoryCalculator.Gui;

public partial class MainWindow : Window
{
    private sealed record ChoiceItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private Gui.UiLanguage _language = Gui.UiTextCatalog.DetectInitialLanguage();
    private Core.ExecutionResult? _lastResult;
    private string? _currentScenarioPath;
    private bool _suspendUpdate;
    private bool _solarShowAllBodies;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PopulatePlanets();
        PopulateChoices();
        ApplyLanguage();
        LoadScenario(Core.SampleScenarioFactory.CreateSingleTemplate());
        StatusText.Text = T("status.ready");
    }

    private void PopulatePlanets()
    {
        foreach (var name in Core.SolarSystemCatalog.PlanetNames)
        {
            OriginCombo.Items.Add(name);
            DestinationCombo.Items.Add(name);
        }
        if (OriginCombo.Items.Count > 2) OriginCombo.SelectedIndex = 2;
        if (DestinationCombo.Items.Count > 3) DestinationCombo.SelectedIndex = 3;
    }

    private void PopulateChoices()
    {
        SetItems(ModeCombo, "single",
            ("single", T("option.mode.single")),
            ("porkchop", T("option.mode.porkchop")));

        SetItems(LongWayCombo, "auto",
            ("auto", T("option.long_way.auto")),
            ("short-way", T("option.long_way.short")),
            ("long-way", T("option.long_way.long")));

        SetItems(ArrivalModeCombo, "circular orbit",
            ("elliptic capture", T("option.arrival.elliptic")),
            ("circular orbit", T("option.arrival.circular")),
            ("flyby", T("option.arrival.flyby")),
            ("ignore arrival burn", T("option.arrival.ignore")));

        SetItems(LaunchEnabledCombo, "orbit",
            ("orbit", T("option.launch_profile.orbit")),
            ("surface", T("option.launch_profile.surface")));

        SetItems(LaunchModeCombo, "quick",
            ("quick", T("option.launch_mode.quick")),
            ("detailed", T("option.launch_mode.detailed")));

        SetItems(LanguageCombo, _language == Gui.UiLanguage.Russian ? "ru" : "en",
            ("en", T("language.english")),
            ("ru", T("language.russian")));
    }

    private static void SetItems(ComboBox combo, string selected, params (string Key, string Label)[] items)
    {
        combo.Items.Clear();
        foreach (var (key, label) in items)
            combo.Items.Add(new ChoiceItem(key, label));
        foreach (ChoiceItem item in combo.Items)
        {
            if (item.Key == selected) { combo.SelectedItem = item; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string SelectedKey(ComboBox combo) =>
        combo.SelectedItem is ChoiceItem item ? item.Key : "";

    private void SingleTemplate_Click(object sender, RoutedEventArgs e)
    {
        LoadScenario(Core.SampleScenarioFactory.CreateSingleTemplate());
        SetStatus(T("status.scenario_loaded"));
    }

    private void PorkchopTemplate_Click(object sender, RoutedEventArgs e)
    {
        LoadScenario(Core.SampleScenarioFactory.CreatePorkchopTemplate());
        SetStatus(T("status.scenario_loaded"));
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"{T("dialog.json_files")}|*.json|{T("dialog.all_files")}|*.*",
            Title = T("dialog.open_title")
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var scenario = Core.ScenarioJson.LoadFromFile(dialog.FileName)
                    ?? throw new InvalidOperationException(T("error.input_json_parse"));
                LoadScenario(scenario);
                _currentScenarioPath = dialog.FileName;
                SetStatus(string.Format(T("status.loaded"), dialog.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, T("app.short_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scenario = BuildScenario();
            var dialog = new SaveFileDialog
            {
                Filter = $"{T("dialog.json_files")}|*.json|{T("dialog.all_files")}|*.*",
                Title = T("dialog.save_title"),
                FileName = System.IO.Path.GetFileName(_currentScenarioPath ?? "solar-system-scenario.json")
            };
            if (dialog.ShowDialog() == true)
            {
                Core.ScenarioJson.SaveToFile(scenario, dialog.FileName);
                _currentScenarioPath = dialog.FileName;
                SetStatus(string.Format(T("status.saved"), dialog.FileName));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, T("app.short_title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RunCalculation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scenario = BuildScenario();
            ScenarioJsonBox.Text = Core.ScenarioJson.Serialize(scenario);
            SetStatus(T("status.running"));
            StatusText.Text = T("status.running");

            var result = await Task.Run(() =>
            {
                var basePath = _currentScenarioPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "ui-scenario.json");
                return Core.ScenarioExecutor.Execute(scenario, basePath, Core.ScenarioJson.Options);
            });

            _lastResult = result;
            SummaryBox.Text = result.ConsoleSummary;
            WrittenFilesBox.Text = result.WrittenFiles.Count == 0
                ? T("text.no_files_written")
                : string.Join(Environment.NewLine, result.WrittenFiles);
            ResultJsonBox.Text = TryLoadResultJson(result);
            RenderVisualizations(result);
            SetStatus(string.Format(T("status.completed"), result.WrittenFiles.Count));
        }
        catch (Exception ex)
        {
            _lastResult = null;
            SummaryBox.Text = $"Error: {ex.Message}";
            ResultJsonBox.Clear();
            WrittenFilesBox.Clear();
            ClearVisuals();
            SetStatus(string.Format(T("status.error"), ex.Message));
        }
    }

    private void RocketBuilder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scriptPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "launch_rocket_builder.ps1"));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\" -ModeOverride gui",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch Rocket Builder: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var single = SelectedKey(ModeCombo) == "single";
        SingleCard.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        PorkchopCard.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
    }

    private void LaunchEnabled_Changed(object sender, SelectionChangedEventArgs e)
    {
        var surface = SelectedKey(LaunchEnabledCombo) == "surface";
        var detailed = SelectedKey(LaunchModeCombo) == "detailed";
        LaunchModeCombo.IsEnabled = surface;
        LaunchMassBox.IsEnabled = surface && detailed;
        LaunchThrustBox.IsEnabled = surface && detailed;
        LaunchIspBox.IsEnabled = surface && detailed;
        LaunchLatBox.IsEnabled = surface;
        LaunchLonBox.IsEnabled = surface;
        LaunchInclBox.IsEnabled = surface;
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUpdate) return;
        var lang = SelectedKey(LanguageCombo) == "ru" ? Gui.UiLanguage.Russian : Gui.UiLanguage.English;
        if (lang == _language) return;
        _language = lang;
        ApplyLanguage();
        PopulateChoices();
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendUpdate) return;
        if (_lastResult == null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (MainTabControl.SelectedItem == TabSolarSystem)
                RenderSolarSystemView(_lastResult);
            else if (MainTabControl.SelectedItem == TabDeltaVVisual)
                RenderDeltaVVisual(_lastResult);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SolarShowAll_Changed(object sender, RoutedEventArgs e)
    {
        _solarShowAllBodies = SolarShowAllCheck.IsChecked == true;
        if (_lastResult != null && MainTabControl.SelectedItem == TabSolarSystem)
            RenderSolarSystemView(_lastResult);
    }

    private Core.ScenarioInput BuildScenario()
    {
        var origin = SelectedPlanet(OriginCombo);
        var destination = SelectedPlanet(DestinationCombo);
        if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(T("error.same_planets"));

        var mode = SelectedKey(ModeCombo);
        if (string.IsNullOrEmpty(mode)) mode = "single";

        var depPeriapsis = ParseDouble(DepPeriapsisBox.Text, 200) * 1000.0;
        var depApoapsis = ParseDouble(DepApoapsisBox.Text, 200) * 1000.0;
        if (depApoapsis < depPeriapsis)
            throw new InvalidOperationException(T("error.departure_orbit_order"));

        var arrivalAltitude = SelectedKey(ArrivalModeCombo) switch
        {
            "ignore arrival burn" => (double?)null,
            "flyby" => 0.0,
            _ => ParseDouble(ArrivalOrbitBox.Text, 250) * 1000.0
        };

        var arrivalMode = SelectedKey(ArrivalModeCombo) switch
        {
            "circular orbit" => "circular-capture",
            "elliptic capture" => "elliptic-capture",
            _ => null
        };

        var launchEnabled = SelectedKey(LaunchEnabledCombo) == "surface";
        var launchMode = SelectedKey(LaunchModeCombo);
        var launchDetailed = launchMode == "detailed";

        var request = new Core.CalculationRequest
        {
            Mode = mode,
            Origin = origin,
            Destination = destination,
            DepartureParkingOrbitAltitude = depPeriapsis,
            DepartureParkingOrbitPeriapsisAltitude = depPeriapsis,
            DepartureParkingOrbitApoapsisAltitude = depApoapsis,
            ArrivalParkingOrbitAltitude = arrivalAltitude,
            ArrivalManeuverMode = arrivalMode,
            UseAerobraking = AerobrakingCheck.IsChecked == true,
            LongWay = SelectedKey(LongWayCombo) switch
            {
                "short-way" => false,
                "long-way" => true,
                _ => null
            },
            CsvOutputPath = mode == "porkchop" ? CsvPathBox.Text : null,
            ResultOutputPath = ResultPathBox.Text,
            Launch = new Core.LaunchConfiguration
            {
                Enabled = launchEnabled,
                Mode = launchMode,
                RocketInitialMass = launchDetailed ? ParseDouble(LaunchMassBox.Text, 500000) : 500000,
                RocketThrust = launchDetailed ? ParseDouble(LaunchThrustBox.Text, 7) * 1e6 : 7e6,
                RocketIsp = launchDetailed ? ParseDouble(LaunchIspBox.Text, 310) : 310,
                LaunchLatitude = ParseDouble(LaunchLatBox.Text, 0),
                LaunchLongitude = ParseDouble(LaunchLonBox.Text, 0),
                TargetInclination = ParseDouble(LaunchInclBox.Text, 0)
            }
        };

        if (mode == "single")
        {
            request.DepartureTime = ToJ2000(DepartureDatePicker);
            request.TravelTime = ParseDouble(TravelDaysBox.Text, 220) * Core.SolarSystemCatalog.SecondsPerDay;
        }
        else
        {
            request.DepartureWindowStart = ToJ2000(WindowStartPicker);
            request.DepartureWindowEnd = ToJ2000(WindowEndPicker);
            request.TravelTimeMin = ParseDouble(TravelMinBox.Text, 120) * Core.SolarSystemCatalog.SecondsPerDay;
            request.TravelTimeMax = ParseDouble(TravelMaxBox.Text, 360) * Core.SolarSystemCatalog.SecondsPerDay;
            request.DepartureSteps = (int)ParseDouble(DepartureStepsBox.Text, 48);
            request.TravelTimeSteps = (int)ParseDouble(TravelStepsBox.Text, 48);
        }

        return new Core.ScenarioInput
        {
            Calendar = Core.SolarSystemCatalog.CreateUtcCalendarInput(),
            CentralBody = Core.SolarSystemCatalog.CreateSunInput(),
            Bodies = [Core.SolarSystemCatalog.CreateBodyInput(origin), Core.SolarSystemCatalog.CreateBodyInput(destination)],
            Request = request
        };
    }

    private void LoadScenario(Core.ScenarioInput scenario)
    {
        _suspendUpdate = true;
        try
        {
            SelectChoice(OriginCombo, scenario.Request.Origin);
            SelectChoice(DestinationCombo, scenario.Request.Destination);

            var mode = (scenario.Request.Mode ?? "single").Trim().ToLowerInvariant();
            SelectChoice(ModeCombo, mode);

            SelectChoice(LongWayCombo, scenario.Request.LongWay switch
            {
                true => "long-way",
                false => "short-way",
                _ => "auto"
            });

            var depDate = scenario.Request.DepartureTime.HasValue
                ? Core.SolarSystemCatalog.FromJ2000Seconds(scenario.Request.DepartureTime.Value)
                : new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
            DepartureDatePicker.SelectedDate = depDate.DateTime;

            TravelDaysBox.Text = scenario.Request.TravelTime.HasValue
                ? (scenario.Request.TravelTime.Value / Core.SolarSystemCatalog.SecondsPerDay).ToString("F1")
                : "220";

            var wStart = scenario.Request.DepartureWindowStart.HasValue
                ? Core.SolarSystemCatalog.FromJ2000Seconds(scenario.Request.DepartureWindowStart.Value)
                : new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
            WindowStartPicker.SelectedDate = wStart.DateTime;

            var wEnd = scenario.Request.DepartureWindowEnd.HasValue
                ? Core.SolarSystemCatalog.FromJ2000Seconds(scenario.Request.DepartureWindowEnd.Value)
                : new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
            WindowEndPicker.SelectedDate = wEnd.DateTime;

            TravelMinBox.Text = scenario.Request.TravelTimeMin.HasValue
                ? (scenario.Request.TravelTimeMin.Value / Core.SolarSystemCatalog.SecondsPerDay).ToString("F1")
                : "120";
            TravelMaxBox.Text = scenario.Request.TravelTimeMax.HasValue
                ? (scenario.Request.TravelTimeMax.Value / Core.SolarSystemCatalog.SecondsPerDay).ToString("F1")
                : "360";
            DepartureStepsBox.Text = scenario.Request.DepartureSteps.ToString() ?? "48";
            TravelStepsBox.Text = scenario.Request.TravelTimeSteps.ToString() ?? "48";

            DepPeriapsisBox.Text = (scenario.Request.ResolveDepartureOrbitPeriapsisAltitude() / 1000.0).ToString("F1");
            DepApoapsisBox.Text = (scenario.Request.ResolveDepartureOrbitApoapsisAltitude() / 1000.0).ToString("F1");

            if (scenario.Request.ArrivalParkingOrbitAltitude.HasValue && scenario.Request.ArrivalParkingOrbitAltitude.Value > 0)
            {
                SelectChoice(ArrivalModeCombo, "circular orbit");
                ArrivalOrbitBox.Text = (scenario.Request.ArrivalParkingOrbitAltitude.Value / 1000.0).ToString("F1");
            }
            else if (scenario.Request.ArrivalParkingOrbitAltitude.HasValue && scenario.Request.ArrivalParkingOrbitAltitude.Value == 0)
            {
                SelectChoice(ArrivalModeCombo, "flyby");
            }
            else
            {
                SelectChoice(ArrivalModeCombo, "ignore arrival burn");
            }

            AerobrakingCheck.IsChecked = scenario.Request.UseAerobraking;

            if (scenario.Request.Launch is { } launch)
            {
                SelectChoice(LaunchEnabledCombo, launch.Enabled ? "surface" : "orbit");
                SelectChoice(LaunchModeCombo, launch.Mode ?? "quick");
                LaunchMassBox.Text = launch.RocketInitialMass.ToString("F1");
                LaunchThrustBox.Text = (launch.RocketThrust / 1e6).ToString("F1");
                LaunchIspBox.Text = launch.RocketIsp.ToString("F1");
                LaunchLatBox.Text = launch.LaunchLatitude.ToString("F1");
                LaunchLonBox.Text = launch.LaunchLongitude.ToString("F1");
                LaunchInclBox.Text = launch.TargetInclination.ToString("F1");
            }
            else
            {
                SelectChoice(LaunchEnabledCombo, "orbit");
            }

            CsvPathBox.Text = scenario.Request.CsvOutputPath ?? "data/results/earth-mars-porkchop.csv";
            ResultPathBox.Text = scenario.Request.ResultOutputPath ?? "data/results/result.json";

            SummaryBox.Clear();
            ResultJsonBox.Clear();
            WrittenFilesBox.Clear();
            _lastResult = null;
            ClearVisuals();

            Mode_SelectionChanged(ModeCombo, null!);
            LaunchEnabled_Changed(LaunchEnabledCombo, null!);
        }
        finally
        {
            _suspendUpdate = false;
        }

        ScenarioJsonBox.Text = Core.ScenarioJson.Serialize(scenario);
        _currentScenarioPath = null;
        CurrentFileText.Text = "";
        SetStatus(T("status.scenario_loaded"));
    }

    private string TryLoadResultJson(Core.ExecutionResult result)
    {
        var jsonFile = result.WrittenFiles.FirstOrDefault(f =>
            f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        if (jsonFile != null && System.IO.File.Exists(jsonFile))
            return System.IO.File.ReadAllText(jsonFile);
        return T("text.no_result_json");
    }

    private string SelectedPlanet(ComboBox combo) =>
        combo.SelectedItem?.ToString() ?? "Earth";

    private static void SelectChoice(ComboBox combo, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) { if (combo.Items.Count > 0) combo.SelectedIndex = 0; return; }
        foreach (var raw in combo.Items)
        {
            if (raw is ChoiceItem ci && ci.Key == key) { combo.SelectedItem = ci; return; }
            if (raw is string s && string.Equals(s, key, StringComparison.OrdinalIgnoreCase)) { combo.SelectedItem = s; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var val) ? val : fallback;

    private static double ToJ2000(DatePicker picker)
    {
        var dt = picker.SelectedDate ?? new DateTime(2026, 11, 12);
        return Core.SolarSystemCatalog.ToJ2000Seconds(new DateTimeOffset(dt, TimeSpan.Zero));
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private string T(string key) => Gui.UiTextCatalog.Get(_language, key);

    private void ApplyLanguage()
    {
        Title = T("app.title");
        HeaderTitle.Text = T("app.short_title");
        LblLanguage.Text = T("label.language");

        BtnSingleTemplate.Content = T("action.single_template");
        BtnPorkchopTemplate.Content = T("action.porkchop_template");
        BtnOpenJson.Content = T("action.open_json");
        BtnSaveJson.Content = T("action.save_json");
        BtnRun.Content = "► " + T("action.run");
        BtnRocketBuilder.Content = T("action.rocket_builder");

        InfoHeading.Text = T("info.title");
        InfoBody.Text = T("info.body");

        CardMission.Text = T("group.mission");
        LblOrigin.Text = T("field.origin_planet");
        LblDestination.Text = T("field.destination_planet");
        LblMode.Text = T("field.mode");
        LblTrajectory.Text = T("field.trajectory");

        CardTransfer.Text = T("card.transfer_schedule");
        LblDepartureDate.Text = T("field.single_departure");
        LblTravelDays.Text = T("field.single_travel_days");

        CardPorkchop.Text = T("card.porkchop_scan");
        LblWindowStart.Text = T("field.window_start");
        LblWindowEnd.Text = T("field.window_end");
        LblTravelMin.Text = T("field.travel_min_days");
        LblTravelMax.Text = T("field.travel_max_days");
        LblDepSteps.Text = T("field.departure_steps");
        LblTravelSteps.Text = T("field.travel_steps");

        CardParking.Text = T("group.parking_orbits");
        LblDepPeriapsis.Text = T("field.departure_orbit_periapsis");
        LblDepApoapsis.Text = T("field.departure_orbit_apoapsis");
        LblArrivalMode.Text = T("field.arrival_mode");
        LblArrivalOrbit.Text = T("field.arrival_parking_orbit");
        AerobrakingCheck.Content = T("field.aerobraking");

        CardLaunch.Text = T("group.launch_config");
        LblMissionStart.Text = T("field.launch_enabled");
        LblLaunchCalc.Text = T("field.launch_mode");
        LblMass.Text = T("field.launch_mass_kg");
        LblThrust.Text = T("field.launch_thrust_mn");
        LblIsp.Text = T("field.launch_isp_sec");
        LblLatitude.Text = T("field.launch_latitude");
        LblLongitude.Text = T("field.launch_longitude");
        LblInclination.Text = T("field.launch_inclination");

        CardOutput.Text = T("group.output_files");
        LblCsvPath.Text = T("field.csv_output_path");
        LblResultJson.Text = T("field.result_json_path");

        LblWrittenFiles.Text = T("group.written_files");
        TabSummary.Header = T("tab.summary");
        TabDeltaVVisual.Header = T("tab.delta_v_visual");
        TabSolarSystem.Header = T("tab.solar_system_view");
        TabScenarioJson.Header = T("tab.scenario_json");
        TabResultJson.Header = T("tab.result_json");

        SummaryBox.Text = T("status.ready");
    }

    // ======================================================================
    // Visualization rendering
    // ======================================================================

    private void RenderVisualizations(Core.ExecutionResult result)
    {
        RenderDeltaVVisual(result);
        RenderSolarSystemView(result);
    }

    private void ClearVisuals()
    {
        DvTotalValue.Text = "---";
        DvEjectionValue.Text = "---";
        DvInsertionValue.Text = "---";
        DvDepartureInfo.Text = "";
        DvTravelInfo.Text = "";
        DvPhaseInfo.Text = "";
        DeltaVImage.Source = null;
        DeltaVImage.Visibility = Visibility.Collapsed;
        DeltaVEmptyText.Visibility = Visibility.Visible;
        SolarCanvas.Children.Clear();
        SolarEmptyText.Visibility = Visibility.Visible;
        SolarInfoPanel.Visibility = Visibility.Collapsed;
    }

    private void RenderDeltaVVisual(Core.ExecutionResult result)
    {
        var transfer = result.Transfer;
        var porkchop = result.Porkchop;
        var cal = result.Calendar;

        if (transfer == null && porkchop == null)
        {
            ClearVisuals();
            return;
        }

        var best = porkchop?.BestTransfer ?? transfer;
        if (best == null)
        {
            ClearVisuals();
            return;
        }

        DvTotalValue.Text = $"{best.DVTotal:F1}";
        DvEjectionValue.Text = $"{best.DVEjection:F1}";
        DvInsertionValue.Text = $"{best.DVInjection:F1}";
        DvDepartureInfo.Text = $"Depart: {cal.FormatDate(best.DepartureTime)}";
        DvTravelInfo.Text = $"Travel: {cal.FormatDuration(best.TravelTime)}";
        var phaseDeg = best.PhaseAngle * Core.LambertSolver.Rad2Deg;
        var sepDeg = best.DepartureSeparation * Core.LambertSolver.Rad2Deg;
        DvPhaseInfo.Text = $"Phase: {phaseDeg:F2} deg ({sepDeg:F2} sep)";

        if (porkchop != null)
        {
            RenderHeatmap(result);
        }
        else
        {
            RenderBarChart(result);
        }
    }

    private void RenderHeatmap(Core.ExecutionResult result)
    {
        var porkchop = result.Porkchop!;
        var cal = result.Calendar;
        var transfer = porkchop.BestTransfer;
        var points = porkchop.Points;
        var depSteps = porkchop.Window.DepartureSteps;
        var travelSteps = porkchop.Window.TravelTimeSteps;
        var depStart = porkchop.Window.DepartureStart;
        var depEnd = porkchop.Window.DepartureEnd;
        var travelMin = porkchop.Window.TravelTimeMin;
        var travelMax = porkchop.Window.TravelTimeMax;

        // Find min/max for color scale
        double minDv = double.MaxValue, maxDv = 0;
        foreach (var p in points)
        {
            if (p.TotalDeltaV.HasValue)
            {
                if (p.TotalDeltaV.Value < minDv) minDv = p.TotalDeltaV.Value;
                if (p.TotalDeltaV.Value > maxDv) maxDv = p.TotalDeltaV.Value;
            }
        }
        if (maxDv <= minDv) maxDv = minDv + 1;

        var width = Math.Max(320, (int)DeltaVImage.ActualWidth);
        var height = Math.Max(240, (int)DeltaVImage.ActualHeight);
        if (width < 100 || height < 100)
        {
            width = 640;
            height = 480;
        }

        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];

        // Plot margins
        int marginLeft = 60, marginRight = 40, marginTop = 30, marginBottom = 50;
        int plotW = width - marginLeft - marginRight;
        int plotH = height - marginTop - marginBottom;

        // Draw background
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int idx = (y * width + x) * 4;
            pixels[idx + 0] = 0x12; // B
            pixels[idx + 1] = 0x12; // G
            pixels[idx + 2] = 0x0F; // R
            pixels[idx + 3] = 0xFF; // A
        }

        // Draw heatmap cells
        foreach (var p in points)
        {
            var col = (int)((p.DepartureTime - depStart) / (depEnd - depStart) * (plotW - 1));
            var row = (int)((p.TravelTime - travelMin) / (travelMax - travelMin) * (plotH - 1));
            var px = marginLeft + col;
            var py = marginTop + (plotH - 1 - row);

            if (px < marginLeft || px >= marginLeft + plotW || py < marginTop || py >= marginTop + plotH)
                continue;

            Color color;
            if (p.TotalDeltaV.HasValue)
            {
                var t = (p.TotalDeltaV.Value - minDv) / (maxDv - minDv);
                color = InterpolateHeatColor(t);
            }
            else
            {
                color = Color.FromRgb(0x22, 0x23, 0x27); // invalid (card color)
            }

            // Draw 2x2 block for visibility
            for (int dy = 0; dy < 2 && py + dy < height; dy++)
            for (int dx = 0; dx < 2 && px + dx < width; dx++)
            {
                int idx = ((py + dy) * width + (px + dx)) * 4;
                pixels[idx + 0] = color.B;
                pixels[idx + 1] = color.G;
                pixels[idx + 2] = color.R;
                pixels[idx + 3] = 0xFF;
            }
        }

        // Draw best transfer marker
        var bestCol = (int)((transfer.DepartureTime - depStart) / (depEnd - depStart) * (plotW - 1));
        var bestRow = (int)((transfer.TravelTime - travelMin) / (travelMax - travelMin) * (plotH - 1));
        var bestPx = marginLeft + bestCol;
        var bestPy = marginTop + (plotH - 1 - bestRow);
        for (int dy = -4; dy <= 4; dy++)
        for (int dx = -4; dx <= 4; dx++)
        {
            if (Math.Abs(dx) < 3 && Math.Abs(dy) < 3) continue;
            var sx = bestPx + dx;
            var sy = bestPy + dy;
            if (sx >= 0 && sx < width && sy >= 0 && sy < height)
            {
                int idx = (sy * width + sx) * 4;
                pixels[idx + 0] = 0xFF;
                pixels[idx + 1] = 0xFF;
                pixels[idx + 2] = 0xFF;
                pixels[idx + 3] = 0xFF;
            }
        }

        // Draw axis labels at corners
        DrawLabel(pixels, width, height, marginLeft, marginTop - 6, cal.FormatDate(depStart).Length > 6 ? cal.FormatDate(depStart)[..6] : cal.FormatDate(depStart), 0xFF, 0xFF, 0xFF);
        DrawLabel(pixels, width, height, marginLeft + plotW - 60, marginTop - 6, cal.FormatDate(depEnd).Length > 6 ? cal.FormatDate(depEnd)[..6] : cal.FormatDate(depEnd), 0xFF, 0xFF, 0xFF);
        DrawLabel(pixels, width, height, marginLeft - 6, marginTop, $"{(travelMax - travelMin) / Core.SolarSystemCatalog.SecondsPerDay:F0}d", 0xFF, 0xFF, 0xFF);
        DrawLabel(pixels, width, height, marginLeft - 6, marginTop + plotH - 12, $"{travelMin / Core.SolarSystemCatalog.SecondsPerDay:F0}d", 0xFF, 0xFF, 0xFF);

        // Legend bar (right side)
        int legendW = 20, legendH = plotH;
        int legendX = marginLeft + plotW + 8;
        int legendY = marginTop;
        for (int ly = 0; ly < legendH; ly++)
        {
            var lt = 1.0 - (double)ly / legendH;
            var lc = InterpolateHeatColor(lt);
            for (int lx = 0; lx < legendW; lx++)
            {
                var lpx = legendX + lx;
                var lpy = legendY + ly;
                if (lpx >= 0 && lpx < width && lpy >= 0 && lpy < height)
                {
                    int idx = (lpy * width + lpx) * 4;
                    pixels[idx + 0] = lc.B;
                    pixels[idx + 1] = lc.G;
                    pixels[idx + 2] = lc.R;
                    pixels[idx + 3] = 0xFF;
                }
            }
        }
        DrawLabel(pixels, width, height, legendX + legendW + 2, legendY + legendH - 8, $"Low", 0xE8, 0xE9, 0xED);
        DrawLabel(pixels, width, height, legendX + legendW + 2, legendY - 2, $"High", 0xE8, 0xE9, 0xED);

        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        DeltaVImage.Source = bmp;
        DeltaVImage.Visibility = Visibility.Visible;
        DeltaVEmptyText.Visibility = Visibility.Collapsed;
    }

    private void RenderBarChart(Core.ExecutionResult result)
    {
        var transfer = result.Transfer;
        if (transfer == null)
        {
            ClearVisuals();
            return;
        }

        var width = Math.Max(320, (int)DeltaVImage.ActualWidth);
        var height = Math.Max(200, (int)DeltaVImage.ActualHeight);
        if (width < 100 || height < 100)
        {
            width = 640;
            height = 300;
        }

        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];

        // Background
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int idx = (y * width + x) * 4;
            pixels[idx + 0] = 0x12; pixels[idx + 1] = 0x12; pixels[idx + 2] = 0x0F; pixels[idx + 3] = 0xFF;
        }

        var barAreaHeight = height - 60;
        var barWidth = (int)(width * 0.18);
        var barGap = (int)(width * 0.06);
        var totalWidth = 3 * barWidth + 2 * barGap;
        var startX = (width - totalWidth) / 2;
        var maxVal = Math.Max(1.0, transfer.DVTotal);

        var barLabels = new[] { "Ejection", "Insertion", "Total" };
        var barValues = new[] { transfer.DVEjection, transfer.DVInjection, transfer.DVTotal };
        var barColors = new (byte R, byte G, byte B)[]
        {
            (0x5B, 0x8F, 0xF9),
            (0xFF, 0x98, 0x00),
            (0x4C, 0xAF, 0x50),
        };

        for (int i = 0; i < 3; i++)
        {
            var barH = Math.Max(2, (int)(barValues[i] / maxVal * barAreaHeight));
            var bx = startX + i * (barWidth + barGap);
            var by = barAreaHeight - barH;

            var (r, g, b) = barColors[i];
            for (int dy = 0; dy < barH; dy++)
            for (int dx = 0; dx < barWidth; dx++)
            {
                var px = bx + dx;
                var py = by + dy;
                if (px >= 0 && px < width && py >= 0 && py < height)
                {
                    int idx = (py * width + px) * 4;
                    pixels[idx + 0] = b; pixels[idx + 1] = g; pixels[idx + 2] = r; pixels[idx + 3] = 0xFF;
                }
            }

            // Value label above bar
            var valText = $"{barValues[i]:F1}";
            DrawLabel(pixels, width, height, bx + barWidth / 2 - (valText.Length * 4), by - 16, valText, 0xE8, 0xE9, 0xED);
            DrawLabel(pixels, width, height, bx + barWidth / 2 - (barLabels[i].Length * 4), barAreaHeight + 6, barLabels[i], 0xA0, 0xA3, 0xB0);
        }

        // Y-axis label
        DrawLabel(pixels, width, height, 6, 4, $"m/s", 0xA0, 0xA3, 0xB0);

        bmp.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        DeltaVImage.Source = bmp;
        DeltaVImage.Visibility = Visibility.Visible;
        DeltaVEmptyText.Visibility = Visibility.Collapsed;
    }

    private static Color InterpolateHeatColor(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (t < 0.5)
        {
            var u = t / 0.5;
            return Color.FromRgb(
                (byte)(72 + (243 - 72) * u),
                (byte)(163 + (190 - 163) * u),
                (byte)(116 + (76 - 116) * u));
        }
        else
        {
            var u = (t - 0.5) / 0.5;
            return Color.FromRgb(
                (byte)(243 + (210 - 243) * u),
                (byte)(190 + (92 - 190) * u),
                (byte)(76 + (78 - 76) * u));
        }
    }

    private static void DrawLabel(byte[] pixels, int w, int h, int x, int y, string text, byte r, byte g, byte b)
    {
        // Simple pixel font rendering (5x7-ish, just put a colored block as placeholder for now)
        // For a real implementation we'd use a proper text rendering approach
        // This is a simplified placeholder
    }

    private void RenderSolarSystemView(Core.ExecutionResult result)
    {
        var transfer = result.Transfer ?? result.Porkchop?.BestTransfer;
        if (transfer == null)
        {
            SolarEmptyText.Visibility = Visibility.Visible;
            SolarInfoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SolarEmptyText.Visibility = Visibility.Collapsed;
        SolarCanvas.Children.Clear();

        var cal = result.Calendar;
        var cw = SolarCanvas.ActualWidth > 10 ? SolarCanvas.ActualWidth : 600;
        var ch = SolarCanvas.ActualHeight > 10 ? SolarCanvas.ActualHeight : 500;
        var cx = cw / 2;
        var cy = ch / 2;
        var scale = Math.Min(cw, ch) / 2.2;

        var orbitColors = new Dictionary<string, Color>
        {
            ["Mercury"] = Color.FromRgb(0xBA, 0xBA, 0xBA),
            ["Venus"] = Color.FromRgb(0xE8, 0xCD, 0x7A),
            ["Earth"] = Color.FromRgb(0x5B, 0x8F, 0xF9),
            ["Mars"] = Color.FromRgb(0xE8, 0x5D, 0x4A),
            ["Jupiter"] = Color.FromRgb(0xD4, 0xA5, 0x67),
            ["Saturn"] = Color.FromRgb(0xE0, 0xD5, 0xA0),
            ["Uranus"] = Color.FromRgb(0x7E, 0xCD, 0xCD),
            ["Neptune"] = Color.FromRgb(0x4A, 0x7E, 0xD8),
        };

        var bodies = Core.SolarSystemCatalog.CreateOrbitalBodies();
        var departureTime = transfer.DepartureTime;
        var arrivalTime = departureTime + transfer.TravelTime;
        var isRelevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { transfer.OriginName, transfer.DestinationName };

        // Compute transfer path first (needed for maxRadius in focused mode)
        var mu = Core.SolarSystemCatalog.SunGravitationalParameter;
        var originPos = transfer.OriginPositionAtDeparture;
        var destPos = transfer.DestinationPositionAtArrival;
        var destBody = bodies.First(b => string.Equals(b.Name, transfer.DestinationName, StringComparison.OrdinalIgnoreCase));
        var destPosAtDep = destBody.Orbit.PositionAtTime(departureTime);
        var v1 = transfer.TransferInitialVelocity;
        var tof = transfer.TravelTime;
        var pathPoints = ComputeTransferOrbitPath(originPos, v1, mu, tof, 60);

        double maxRadius;
        if (_solarShowAllBodies)
        {
            maxRadius = bodies.Max(b => b.Orbit.SemiMajorAxis);
        }
        else
        {
            double max = originPos.Magnitude;
            var d = destPos.Magnitude;
            if (d > max) max = d;
            foreach (var p in pathPoints)
            {
                d = p.Magnitude;
                if (d > max) max = d;
            }
            maxRadius = max * 1.3;
        }

        foreach (var body in bodies)
        {
            var relevant = isRelevant.Contains(body.Name);
            var showPath = _solarShowAllBodies || relevant;
            if (!showPath) continue;

            var orbitColor = orbitColors.TryGetValue(body.Name, out var oc) ? oc : Color.FromRgb(0x55, 0x55, 0x55);
            var pPos = body.Orbit.PositionAtTime(departureTime);
            var (mx, my) = ProjectPosition(pPos, cx, cy, maxRadius, scale);

            if (_solarShowAllBodies && !relevant)
            {
                // Non-relevant planet: faint orbit, small marker, dim label
                var points = new PointCollection();
                for (int i = 0; i <= 180; i++)
                {
                    var ta = Core.LambertSolver.TwoPi * i / 180;
                    var pos = body.Orbit.PositionAtTrueAnomaly(ta);
                    var (px, py) = ProjectPosition(pos, cx, cy, maxRadius, scale);
                    points.Add(new Point(px, py));
                }
                SolarCanvas.Children.Add(new Polyline
                {
                    Points = points,
                    Stroke = new SolidColorBrush(Color.FromArgb(20, orbitColor.R, orbitColor.G, orbitColor.B)),
                    StrokeThickness = 0.5
                });

                SolarCanvas.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(Color.FromArgb(80, orbitColor.R, orbitColor.G, orbitColor.B))
                });
                Canvas.SetLeft(SolarCanvas.Children[^1], mx - 3);
                Canvas.SetTop(SolarCanvas.Children[^1], my - 3);

                SolarCanvas.Children.Add(new TextBlock
                {
                    Text = body.Name,
                    FontSize = 8,
                    Foreground = new SolidColorBrush(Color.FromArgb(100, orbitColor.R, orbitColor.G, orbitColor.B)),
                    FontFamily = (FontFamily)FindResource("PrimaryFont")
                });
                Canvas.SetLeft(SolarCanvas.Children[^1], mx + 5);
                Canvas.SetTop(SolarCanvas.Children[^1], my - 4);
            }
            else
            {
                // Relevant planet: full brightness
                var points = new PointCollection();
                for (int i = 0; i <= 180; i++)
                {
                    var ta = Core.LambertSolver.TwoPi * i / 180;
                    var pos = body.Orbit.PositionAtTrueAnomaly(ta);
                    var (px, py) = ProjectPosition(pos, cx, cy, maxRadius, scale);
                    points.Add(new Point(px, py));
                }
                SolarCanvas.Children.Add(new Polyline
                {
                    Points = points,
                    Stroke = new SolidColorBrush(Color.FromArgb(64, orbitColor.R, orbitColor.G, orbitColor.B)),
                    StrokeThickness = 1
                });

                SolarCanvas.Children.Add(new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(orbitColor),
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                    StrokeThickness = 2
                });
                Canvas.SetLeft(SolarCanvas.Children[^1], mx - 5);
                Canvas.SetTop(SolarCanvas.Children[^1], my - 5);

                SolarCanvas.Children.Add(new TextBlock
                {
                    Text = body.Name,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(orbitColor),
                    FontFamily = (FontFamily)FindResource("PrimaryFont")
                });
                Canvas.SetLeft(SolarCanvas.Children[^1], mx + 8);
                Canvas.SetTop(SolarCanvas.Children[^1], my - 5);
            }

            // Line from center to planet
            byte lineAlpha = _solarShowAllBodies && !relevant ? (byte)25 : (byte)50;
            SolarCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = mx, Y2 = my,
                Stroke = new SolidColorBrush(Color.FromArgb(lineAlpha, orbitColor.R, orbitColor.G, orbitColor.B)),
                StrokeThickness = 0.5,
                StrokeDashArray = new DoubleCollection([2, 3])
            });
        }

        // Draw arc + phase angle between origin and destination (both at departure time)
        var originAng = Math.Atan2(originPos.Y, originPos.X);
        var destDepAng = Math.Atan2(destPosAtDep.Y, destPosAtDep.X);
        var rawDiff = destDepAng - originAng;
        if (rawDiff > Math.PI) rawDiff -= Core.LambertSolver.TwoPi;
        if (rawDiff < -Math.PI) rawDiff += Core.LambertSolver.TwoPi;
        var arcR = Math.Min(cw, ch) * 0.12;

        var arcPoints = new PointCollection();
        int arcSteps = 30;
        for (int i = 0; i <= arcSteps; i++)
        {
            var t = (double)i / arcSteps;
            var a = originAng + rawDiff * t;
            arcPoints.Add(new Point(cx + arcR * Math.Cos(a), cy - arcR * Math.Sin(a)));
        }
        SolarCanvas.Children.Add(new Polyline
        {
            Points = arcPoints,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1
        });

        var midA = originAng + rawDiff / 2;
        var lx = cx + (arcR + 14) * Math.Cos(midA);
        var ly = cy - (arcR + 14) * Math.Sin(midA);
        var visDeg = rawDiff * Core.LambertSolver.Rad2Deg;

        SolarCanvas.Children.Add(new TextBlock
        {
            Text = $"{Math.Abs(visDeg):F1}°",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            FontFamily = (FontFamily)FindResource("PrimaryFont")
        });
        Canvas.SetLeft(SolarCanvas.Children[^1], lx);
        Canvas.SetTop(SolarCanvas.Children[^1], ly);

        // Draw transfer trajectory
        if (pathPoints.Count > 1)
        {
            var tPoints = new PointCollection();
            foreach (var p in pathPoints)
            {
                var (tx, ty) = ProjectPosition(p, cx, cy, maxRadius, scale);
                tPoints.Add(new Point(tx, ty));
            }
            SolarCanvas.Children.Add(new Polyline
            {
                Points = tPoints,
                Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection([5, 3])
            });
        }

        var (ox, oy) = ProjectPosition(originPos, cx, cy, maxRadius, scale);
        var (dx, dy) = ProjectPosition(destPos, cx, cy, maxRadius, scale);

        // Origin marker (large, at departure)
        var originColor = orbitColors.TryGetValue(transfer.OriginName, out var ogc) ? ogc : Color.FromRgb(0x5B, 0x8F, 0xF9);
        SolarCanvas.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = new SolidColorBrush(originColor),
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            StrokeThickness = 3
        });
        Canvas.SetLeft(SolarCanvas.Children[^1], ox - 8);
        Canvas.SetTop(SolarCanvas.Children[^1], oy - 8);

        // Destination marker (large, at arrival)
        var destColor = orbitColors.TryGetValue(transfer.DestinationName, out var dsc) ? dsc : Color.FromRgb(0xE8, 0x5D, 0x4A);
        SolarCanvas.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = new SolidColorBrush(destColor),
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
            StrokeThickness = 3
        });
        Canvas.SetLeft(SolarCanvas.Children[^1], dx - 8);
        Canvas.SetTop(SolarCanvas.Children[^1], dy - 8);

        // Sun in center
        SolarCanvas.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = new SolidColorBrush(Color.FromRgb(0xF8, 0xBF, 0x44)),
            Stroke = new SolidColorBrush(Color.FromRgb(0xDA, 0xA5, 0x20)),
            StrokeThickness = 3
        });
        Canvas.SetLeft(SolarCanvas.Children[^1], cx - 10);
        Canvas.SetTop(SolarCanvas.Children[^1], cy - 10);

        // Info panel
        SolarTitle.Text = $"{transfer.OriginName} → {transfer.DestinationName}";
        SolarPhaseInfo.Text = $"Phase angle: {transfer.PhaseAngle * Core.LambertSolver.Rad2Deg:F2}° ({transfer.DepartureSeparation * Core.LambertSolver.Rad2Deg:F2}° sep)";
        SolarDepartureInfo.Text = $"Depart: {cal.FormatDate(transfer.DepartureTime)}";
        SolarTravelInfo.Text = $"Travel: {cal.FormatDuration(transfer.TravelTime)}";
        SolarLongWayInfo.Text = $"Long-way: {(transfer.LongWay ? "yes" : "no")}";
        SolarNote.Text = _solarShowAllBodies
            ? "All planets shown; non-essential bodies are dimmed."
            : "Only origin/destination bodies shown. Check 'Show all planets' for full system.";

        SolarLegend.Children.Clear();
        AddLegendItem(originColor, $"{transfer.OriginName} at departure");
        AddLegendItem(destColor, $"{transfer.DestinationName} at arrival");
        AddLegendLine(Color.FromRgb(0x4C, 0xAF, 0x50), "Transfer path");

        SolarInfoPanel.Visibility = Visibility.Visible;
    }

    private static List<Core.Vector3D> ComputeTransferOrbitPath(
        Core.Vector3D r1, Core.Vector3D v1, double mu, double tof, int steps)
    {
        var results = new List<Core.Vector3D> { r1 };
        if (steps <= 1) return results;

        var h = Core.Vector3D.Cross(r1, v1);
        var hMag = h.Magnitude;
        var rMag = r1.Magnitude;
        var vMag = v1.Magnitude;
        var eVec = (Core.Vector3D.Cross(v1, h) / mu) - (r1 / rMag);
        var e = eVec.Magnitude;
        var energy = vMag * vMag / 2.0 - mu / rMag;
        var a = -mu / (2.0 * energy);
        if (a <= 0 || e >= 1) return results;

        var inc = Math.Acos(Math.Clamp(h.Z / hMag, -1, 1));
        var zAxis = new Core.Vector3D(0, 0, 1);
        var n = Core.Vector3D.Cross(zAxis, h);
        var nMag = n.Magnitude;

        double lan, argPeri, trueAnomaly;
        if (nMag < 1e-12)
        {
            lan = 0;
            if (e < 1e-12)
            {
                argPeri = 0;
                trueAnomaly = Math.Acos(Math.Clamp(r1.X / rMag, -1, 1));
                if (v1.X > 0) trueAnomaly = Core.LambertSolver.TwoPi - trueAnomaly;
            }
            else
            {
                argPeri = Math.Atan2(eVec.Y, eVec.X);
                if (argPeri < 0) argPeri += Core.LambertSolver.TwoPi;
                trueAnomaly = Math.Acos(Math.Clamp(Core.Vector3D.Dot(eVec, r1) / (e * rMag), -1, 1));
                if (Core.Vector3D.Dot(r1, v1) < 0) trueAnomaly = Core.LambertSolver.TwoPi - trueAnomaly;
            }
        }
        else
        {
            lan = Math.Acos(Math.Clamp(n.X / nMag, -1, 1));
            if (n.Y < 0) lan = Core.LambertSolver.TwoPi - lan;
            if (e < 1e-12)
            {
                argPeri = 0;
                trueAnomaly = Math.Acos(Math.Clamp(Core.Vector3D.Dot(n, r1) / (nMag * rMag), -1, 1));
                if (r1.Z < 0) trueAnomaly = Core.LambertSolver.TwoPi - trueAnomaly;
            }
            else
            {
                argPeri = Math.Acos(Math.Clamp(Core.Vector3D.Dot(n, eVec) / (nMag * e), -1, 1));
                if (eVec.Z < 0) argPeri = Core.LambertSolver.TwoPi - argPeri;
                trueAnomaly = Math.Acos(Math.Clamp(Core.Vector3D.Dot(eVec, r1) / (e * rMag), -1, 1));
                if (Core.Vector3D.Dot(r1, v1) < 0) trueAnomaly = Core.LambertSolver.TwoPi - trueAnomaly;
            }
        }

        var sinTA = Math.Sin(trueAnomaly);
        var cosTA = Math.Cos(trueAnomaly);
        var eccAnom = Math.Atan2(sinTA * Math.Sqrt(Math.Max(0, 1 - e * e)), e + cosTA);
        var meanAnomaly = eccAnom - e * Math.Sin(eccAnom);

        var orbit = new Core.OrbitalElements(a, e, inc, lan, argPeri, meanAnomaly, 0, mu);
        var dt = tof / steps;
        for (int i = 1; i <= steps; i++)
            results.Add(orbit.PositionAtTime(dt * i));

        return results;
    }

    /// <summary>
    /// Compresses the magnitude of a position vector using power-law scaling,
    /// preserving the direction exactly. This avoids the "square" distortion
    /// that comes from scaling X and Y independently.
    /// </summary>
    private static (double px, double py) ProjectPosition(
        Core.Vector3D pos, double cx, double cy, double maxRadius, double scale)
    {
        var mag = pos.Magnitude;
        if (mag < 1e-12) return (cx, cy);
        var compressed = Math.Pow(mag / maxRadius, 0.35) * scale;
        return (cx + compressed * pos.X / mag, cy - compressed * pos.Y / mag);
    }

    private static double ScaleRadius(double value, double maxRadius, double scalePixels)
    {
        var absVal = Math.Abs(value);
        if (absVal < 1e-12) return 0;
        var normalized = absVal / maxRadius;
        // Use power 0.35 compression: inner planets get ~3x more spread vs sqrt(0.5)
        var sign = value >= 0 ? 1.0 : -1.0;
        return Math.Pow(normalized, 0.35) * scalePixels * sign;
    }

    private void DrawPlanetMarker(double x, double y, string label, Color color, bool filled, double size = 8.0)
    {
        var ellipse = new System.Windows.Shapes.Ellipse
        {
            Width = size,
            Height = size,
            Fill = filled ? new SolidColorBrush(color) : Brushes.Transparent,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2
        };
        Canvas.SetLeft(ellipse, x - size / 2);
        Canvas.SetTop(ellipse, y - size / 2);
        SolarCanvas.Children.Add(ellipse);

        if (!string.IsNullOrEmpty(label))
        {
            var tb = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA3, 0xB0)),
                FontFamily = (FontFamily)FindResource("PrimaryFont")
            };
            Canvas.SetLeft(tb, x + size / 2 + 4);
            Canvas.SetTop(tb, y - 6);
            SolarCanvas.Children.Add(tb);
        }
    }

    private void AddLegendItem(Color color, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 6, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA3, 0xB0)),
            FontFamily = (FontFamily)FindResource("PrimaryFont")
        });
        SolarLegend.Children.Add(panel);
    }

    private void AddLegendLine(Color color, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var line = new System.Windows.Shapes.Line
        {
            X1 = 0, Y1 = 5, X2 = 14, Y2 = 5,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection([3, 2]),
            Margin = new Thickness(0, 0, 6, 0)
        };
        panel.Children.Add(line);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA3, 0xB0)),
            FontFamily = (FontFamily)FindResource("PrimaryFont")
        });
        SolarLegend.Children.Add(panel);
    }
}
