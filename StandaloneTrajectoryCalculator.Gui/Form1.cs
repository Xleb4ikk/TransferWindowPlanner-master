using Core = StandaloneTrajectoryCalculator;

namespace StandaloneTrajectoryCalculator.Gui;

public partial class Form1 : Form
{
    private sealed class WheelScrollPanel : Panel
    {
        public WheelScrollPanel()
        {
            AutoScroll = true;
        }

        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location;
        }

        public void EnableWheelScroll(Control root)
        {
            HookMouseWheel(root);
        }

        private void HookMouseWheel(Control control)
        {
            control.MouseWheel -= ForwardMouseWheel;
            control.MouseWheel += ForwardMouseWheel;

            foreach (Control child in control.Controls)
            {
                HookMouseWheel(child);
            }

            control.ControlAdded -= ChildControlAdded;
            control.ControlAdded += ChildControlAdded;
        }

        private void ChildControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control is null)
            {
                return;
            }

            HookMouseWheel(e.Control);
        }

        private void ForwardMouseWheel(object? sender, MouseEventArgs e)
        {
            if (!VerticalScroll.Visible)
            {
                return;
            }

            var nextValue = VerticalScroll.Value - e.Delta;
            nextValue = Math.Max(VerticalScroll.Minimum, Math.Min(nextValue, VerticalScroll.Maximum - VerticalScroll.LargeChange + 1));
            VerticalScroll.Value = nextValue;
            PerformLayout();
        }
    }

    private sealed record ChoiceItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private const int LeftPanelWidth = 640;
    private const int LeftPanelMinWidth = 420;
    private const int LabelColumnWidth = 250;
    private const decimal MaxMissionDurationDays = 10000m;
    private static readonly Size PreferredWindowSize = new(1560, 980);
    private static readonly Size MinimumWindowSize = new(1040, 700);

    private readonly ComboBox _origin = DropDown();
    private readonly ComboBox _destination = DropDown();
    private readonly ComboBox _mode = DropDown();
    private readonly ComboBox _longWay = DropDown();
    private readonly DateTimePicker _singleDeparture = UtcPicker();
    private readonly DateTimePicker _windowStart = UtcPicker();
    private readonly DateTimePicker _windowEnd = UtcPicker();
    private readonly NumericUpDown _singleTravelDays = Num(220, 1, MaxMissionDurationDays, 2);
    private readonly NumericUpDown _travelMinDays = Num(120, 1, MaxMissionDurationDays, 2);
    private readonly NumericUpDown _travelMaxDays = Num(360, 1, MaxMissionDurationDays, 2);
    private readonly NumericUpDown _departureSteps = Num(48, 2, 400, 0);
    private readonly NumericUpDown _travelSteps = Num(48, 2, 400, 0);
    private readonly NumericUpDown _departureOrbitPeriapsisKm = Num(200, 0, 2000000, 1);
    private readonly NumericUpDown _departureOrbitApoapsisKm = Num(200, 0, 2000000, 1);
    private readonly ComboBox _arrivalMode = DropDown();
    private readonly NumericUpDown _arrivalOrbitKm = Num(250, 0, 2000000, 1);
    private readonly CheckBox _arrivalAerobraking = new() { AutoSize = true };
    private readonly ComboBox _launchEnabled = DropDown();
    private readonly ComboBox _launchMode = DropDown();
    private readonly NumericUpDown _launchMassKg = Num(500000m, 10000m, 10000000m, 0);
    private readonly NumericUpDown _launchThrustMn = Num(7m, 0.1m, 50m, 1);
    private readonly NumericUpDown _launchIspSec = Num(310m, 50m, 500m, 0);
    private readonly NumericUpDown _launchLatitude = Num(0m, -90m, 90m, 1);
    private readonly NumericUpDown _launchLongitude = Num(0m, -180m, 180m, 1);
    private readonly NumericUpDown _launchInclination = Num(0m, 0m, 180m, 1);
    private readonly ComboBox _language = DropDown();
    private readonly TextBox _csvPath = Box("data/results/earth-mars-porkchop.csv");
    private readonly TextBox _resultPath = Box("data/results/result.json");
    private readonly TextBox _currentFile = ReadOnlyLine();
    private readonly TextBox _originDetails = ReadOnlyArea();
    private readonly TextBox _destinationDetails = ReadOnlyArea();
    private readonly RichTextBox _summary = SummaryBox();
    private readonly TextBox _scenarioJson = CodeBox();
    private readonly TextBox _resultJson = CodeBox();
    private readonly TextBox _writtenFiles = CodeBox();
    private readonly DeltaVVisualizationControl _deltaVVisual = new() { Dock = DockStyle.Fill };
    private readonly SolarSystemViewControl _solarSystemView = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) };

    private TableLayoutPanel? _infoCardLayout;
    private WheelScrollPanel? _leftScrollHost;
    private TableLayoutPanel? _leftContent;
    private string? _currentScenarioPath;
    private Core.ExecutionResult? _lastExecutionResult;
    private bool _suspendRefresh;
    private UiLanguage _uiLanguage = UiTextCatalog.DetectInitialLanguage();

    public Form1()
    {
        InitializeComponent();
        MinimumSize = MinimumWindowSize;
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi();
        FitWindowToScreen();
        WireEvents();
        LoadScenarioIntoForm(Core.SampleScenarioFactory.CreateSingleTemplate(), null);
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();
        PopulatePlanetChoices();
        ApplyLocalizedChoices();
        ApplyVisualizationTexts();
        Text = T("app.title");

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = UiTheme.WindowBackground };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 10), BackColor = UiTheme.ToolbarBackground, WrapContents = false, AutoScroll = true };
        actions.Controls.Add(Action(T("action.single_template"), (_, _) => LoadScenarioIntoForm(Core.SampleScenarioFactory.CreateSingleTemplate(), null)));
        actions.Controls.Add(Action(T("action.porkchop_template"), (_, _) => LoadScenarioIntoForm(Core.SampleScenarioFactory.CreatePorkchopTemplate(), null)));
        actions.Controls.Add(Action(T("action.open_json"), (_, _) => OpenScenarioFromJson()));
        actions.Controls.Add(Action(T("action.save_json"), (_, _) => SaveScenarioToJson()));
        actions.Controls.Add(Action(T("action.run"), async (_, _) => await RunCalculationAsync(), UiTheme.AccentGreen));
        actions.Controls.Add(Action(RocketBuilderActionText(), (_, _) => OpenRocketBuilder(), UiTheme.AccentAmber));
        actions.Controls.Add(new Label { AutoSize = true, Text = T("label.language"), Margin = new Padding(20, 10, 8, 0) });
        _language.Width = 120;
        actions.Controls.Add(_language);
        actions.Controls.Add(new Label { AutoSize = true, Text = T("label.current_json"), Margin = new Padding(20, 10, 8, 0) });
        _currentFile.Width = 360;
        actions.Controls.Add(_currentFile);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 690, BackColor = root.BackColor };
        split.Panel1MinSize = LeftPanelMinWidth + 24;
        split.Panel1.Padding = new Padding(16);
        split.Panel2.Padding = new Padding(16);

        var leftScroll = new WheelScrollPanel { Dock = DockStyle.Fill };
        var left = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LeftPanelWidth));

        AddLeftSection(left, InfoCard());
        AddLeftSection(left, Group(T("group.mission"),
            (T("field.origin_planet"), _origin),
            (T("field.destination_planet"), _destination),
            (T("field.mode"), _mode),
            (T("field.long_way"), _longWay),
            (T("field.single_departure"), _singleDeparture),
            (T("field.single_travel_days"), _singleTravelDays),
            (T("field.window_start"), _windowStart),
            (T("field.window_end"), _windowEnd),
            (T("field.travel_min_days"), _travelMinDays),
            (T("field.travel_max_days"), _travelMaxDays),
            (T("field.departure_steps"), _departureSteps),
            (T("field.travel_steps"), _travelSteps)));
        AddLeftSection(left, Group(T("group.parking_orbits"),
            (T("field.departure_orbit_periapsis"), _departureOrbitPeriapsisKm),
            (T("field.departure_orbit_apoapsis"), _departureOrbitApoapsisKm),
            (T("field.arrival_mode"), _arrivalMode),
            (T("field.arrival_parking_orbit"), _arrivalOrbitKm),
            (T("field.arrival_aerobraking"), _arrivalAerobraking)));
        AddLeftSection(left, Group(T("group.launch_config"),
            (T("field.launch_enabled"), _launchEnabled),
            (T("field.launch_mode"), _launchMode),
            (T("field.launch_mass_kg"), _launchMassKg),
            (T("field.launch_thrust_mn"), _launchThrustMn),
            (T("field.launch_isp_sec"), _launchIspSec),
            (T("field.launch_latitude"), _launchLatitude),
            (T("field.launch_longitude"), _launchLongitude),
            (T("field.launch_inclination"), _launchInclination)));
        AddLeftSection(left, Group(T("group.output_files"),
            (T("field.csv_output_path"), _csvPath),
            (T("field.result_json_path"), _resultPath)));
        AddLeftSection(left, PlanetDetailsGroup());
        _leftScrollHost = leftScroll;
        _leftContent = left;
        leftScroll.Controls.Add(left);
        leftScroll.EnableWheelScroll(left);
        leftScroll.Resize += (_, _) => UpdateLeftScrollLayout();
        split.Panel1.Controls.Add(leftScroll);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = UiTheme.PanelBackground };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 78));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Tab(T("tab.summary"), _summary));
        tabs.TabPages.Add(Tab(T("tab.delta_v_visual"), _deltaVVisual));
        tabs.TabPages.Add(Tab(T("tab.solar_system_view"), _solarSystemView));
        tabs.TabPages.Add(Tab(T("tab.scenario_json"), _scenarioJson));
        tabs.TabPages.Add(Tab(T("tab.result_json"), _resultJson));
        var filesGroup = new ThemedGroupBox { Text = T("group.written_files"), Dock = DockStyle.Fill, Padding = new Padding(12, 30, 12, 12) };
        filesGroup.Controls.Add(_writtenFiles);
        right.Controls.Add(tabs, 0, 0);
        right.Controls.Add(filesGroup, 0, 1);
        split.Panel2.Controls.Add(right);

        var statusPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 2), BackColor = UiTheme.ToolbarBackground };
        statusPanel.Controls.Add(_status);

        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(statusPanel, 0, 2);
        Controls.Add(root);
        UpdateLeftScrollLayout();
        UiTheme.Apply(this);
        ResumeLayout(true);
    }

    private void ApplyVisualizationTexts()
    {
        _deltaVVisual.EmptyText = T("visual.empty_delta");
        _deltaVVisual.BestCaptionText = T("visual.delta.best_caption");
        _deltaVVisual.SingleCaptionText = T("visual.delta.single_caption");
        _deltaVVisual.TotalDvText = T("visual.delta.total");
        _deltaVVisual.EjectionDvText = T("visual.delta.ejection");
        _deltaVVisual.InsertionDvText = T("visual.delta.insertion");
        _deltaVVisual.DepartureText = T("visual.delta.departure");
        _deltaVVisual.TravelText = T("visual.delta.travel");
        _deltaVVisual.PhaseAngleText = T("visual.delta.phase");
        _deltaVVisual.GridText = T("visual.delta.grid");
        _deltaVVisual.DepartureAxisText = T("visual.delta.departure_axis");
        _deltaVVisual.TravelAxisText = T("visual.delta.travel_axis");
        _deltaVVisual.LegendLowText = T("visual.delta.legend_low");
        _deltaVVisual.LegendHighText = T("visual.delta.legend_high");
        _deltaVVisual.BestPointText = T("visual.delta.best_point");
        _deltaVVisual.HeatmapTitleText = T("visual.delta.heatmap_title");
        _deltaVVisual.BreakdownTitleText = T("visual.delta.breakdown_title");

        _solarSystemView.EmptyText = T("visual.empty_solar");
        _solarSystemView.PhaseAngleText = T("visual.solar.phase_angle");
        _solarSystemView.DepartureText = T("visual.delta.departure");
        _solarSystemView.TravelText = T("visual.delta.travel");
        _solarSystemView.LongWayText = T("visual.solar.long_way");
        _solarSystemView.YesText = T("text.yes");
        _solarSystemView.NoText = T("text.no");
        _solarSystemView.OriginDepartureText = T("visual.solar.origin_departure");
        _solarSystemView.DestinationDepartureText = T("visual.solar.destination_departure");
        _solarSystemView.DestinationArrivalText = T("visual.solar.destination_arrival");
        _solarSystemView.TransferPathText = T("visual.solar.transfer_path");
        _solarSystemView.ScaleNoteText = T("visual.solar.scale_note");
    }

    private void WireEvents()
    {
        foreach (var control in new Control[] { _origin, _destination, _mode, _longWay, _arrivalMode, _arrivalAerobraking, _launchEnabled, _launchMode, _csvPath, _resultPath, _singleDeparture, _windowStart, _windowEnd, _singleTravelDays, _travelMinDays, _travelMaxDays, _departureSteps, _travelSteps, _departureOrbitPeriapsisKm, _departureOrbitApoapsisKm, _arrivalOrbitKm, _launchMassKg, _launchThrustMn, _launchIspSec, _launchLatitude, _launchLongitude, _launchInclination })
        {
            switch (control)
            {
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => RefreshAll(); break;
                case CheckBox check: check.CheckedChanged += (_, _) => RefreshAll(); break;
                case TextBox text: text.TextChanged += (_, _) => RefreshPreview(); break;
                case DateTimePicker picker: picker.ValueChanged += (_, _) => RefreshPreview(); break;
                case NumericUpDown number: number.ValueChanged += (_, _) => RefreshPreview(); break;
            }
        }
        _origin.SelectedIndexChanged += (_, _) => UpdatePlanetDetails();
        _destination.SelectedIndexChanged += (_, _) => UpdatePlanetDetails();
        _mode.SelectedIndexChanged += (_, _) => RefreshModeState();
        _arrivalMode.SelectedIndexChanged += (_, _) => RefreshModeState();
        _language.SelectedIndexChanged += (_, _) => ChangeLanguage();
    }

    private async Task RunCalculationAsync()
    {
        Core.ScenarioInput scenario;
        try { scenario = BuildScenarioFromForm(); }
        catch (Exception ex) { Error(ex.Message); return; }

        SetStatus(T("status.running"));
        try
        {
            var basePath = _currentScenarioPath ?? Path.Combine(AppContext.BaseDirectory, "ui-scenario.json");
            var result = await Task.Run(() => Core.ScenarioExecutor.Execute(scenario, basePath, Core.ScenarioJson.Options));
            _lastExecutionResult = result;
            _summary.Text = result.ConsoleSummary;
            _writtenFiles.Text = result.WrittenFiles.Count == 0 ? T("text.no_files_written") : string.Join(Environment.NewLine, result.WrittenFiles);
            _resultJson.Text = TryLoadResultJson(basePath, scenario, result.WrittenFiles);
            ApplyVisualizationResult(result);
            SetStatus(F("status.completed", result.WrittenFiles.Count));
        }
        catch (Exception ex)
        {
            _lastExecutionResult = null;
            _summary.Clear();
            _resultJson.Clear();
            _writtenFiles.Clear();
            ClearVisualizationResult();
            Error(ex.Message);
        }
    }

    private void OpenScenarioFromJson()
    {
        using var dialog = new OpenFileDialog { Filter = $"{T("dialog.json_files")}|*.json|{T("dialog.all_files")}|*.*", Title = T("dialog.open_title") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var scenario = Core.ScenarioJson.LoadFromFile(dialog.FileName) ?? throw new InvalidOperationException(T("error.input_json_parse"));
            LoadScenarioIntoForm(scenario, dialog.FileName);
            SetStatus(F("status.loaded", dialog.FileName));
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void SaveScenarioToJson()
    {
        Core.ScenarioInput scenario;
        try { scenario = BuildScenarioFromForm(); }
        catch (Exception ex) { Error(ex.Message); return; }

        using var dialog = new SaveFileDialog { Filter = $"{T("dialog.json_files")}|*.json|{T("dialog.all_files")}|*.*", Title = T("dialog.save_title"), FileName = Path.GetFileName(_currentScenarioPath ?? "solar-system-scenario.json") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Core.ScenarioJson.SaveToFile(scenario, dialog.FileName);
            _currentScenarioPath = dialog.FileName;
            _currentFile.Text = dialog.FileName;
            RefreshPreview();
            SetStatus(F("status.saved", dialog.FileName));
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void OpenRocketBuilder()
    {
        try
        {
            using var form = new RocketBuilderForm(_uiLanguage, CreateRocketBuilderSeed());
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private RocketBuilderMissionRequest CreateRocketBuilderSeed()
    {
        var arrivalAltitude = ResolveArrivalAltitude();
        var departurePeriapsisKm = (double)_departureOrbitPeriapsisKm.Value;
        var departureApoapsisKm = (double)_departureOrbitApoapsisKm.Value;
        var surfaceLaunch = SelectedKey(_launchEnabled, "orbit") == "surface";
        var executionResult = ResolveRocketBuilderExecutionResult();
        var request = new RocketBuilderMissionRequest
        {
            Origin = _origin.SelectedItem?.ToString(),
            Destination = _destination.SelectedItem?.ToString(),
            PayloadMassKg = 1000.0,
            TravelDays = SelectedKey(_mode, "single") == "single" ? (double)_singleTravelDays.Value : (double)_travelMinDays.Value,
            MeanDistanceAu = 1.0,
            MinDistanceAu = 1.0,
            DepartureOrbitKm = departurePeriapsisKm,
            DepartureOrbitPeriapsisKm = departurePeriapsisKm,
            DepartureOrbitApoapsisKm = departureApoapsisKm,
            ArrivalOrbitKm = arrivalAltitude.HasValue && arrivalAltitude.Value > 0.0 ? arrivalAltitude.Value / 1000.0 : null,
            SurfaceLaunch = surfaceLaunch,
            DepartureSurfaceGravityMps2 = RocketBuilderMissionDefaults.ResolveDepartureSurfaceGravityMps2(_origin.SelectedItem?.ToString())
        };

        if (executionResult?.Transfer is { } transfer)
        {
            var departureDistanceAu = transfer.OriginPositionAtDeparture.Magnitude / Core.SolarSystemCatalog.AstronomicalUnit;
            var arrivalDistanceAu = transfer.DestinationPositionAtArrival.Magnitude / Core.SolarSystemCatalog.AstronomicalUnit;
            var meanDistanceAu = (departureDistanceAu + arrivalDistanceAu) * 0.5;
            var minDistanceAu = Math.Min(departureDistanceAu, arrivalDistanceAu);
            var launchDeltaV = surfaceLaunch ? (executionResult.Launch?.RequiredDeltaVMps ?? 0.0) : 0.0;

            request.DvTotalMps = transfer.DVTotal + launchDeltaV;
            request.DvEjectionMps = transfer.DVEjection + launchDeltaV;
            request.DvInjectionMps = transfer.DVInjection;
            request.LaunchAscentDvMps = launchDeltaV > 0.0 ? launchDeltaV : null;
            request.TravelDays = transfer.TravelTime / Core.SolarSystemCatalog.SecondsPerDay;
            request.MeanDistanceAu = meanDistanceAu;
            request.MinDistanceAu = minDistanceAu;
            request.DepartureDistanceAu = departureDistanceAu;
            request.ArrivalDistanceAu = arrivalDistanceAu;
            request.DepartureSolarFluxWm2 = 1361.0 / Math.Pow(Math.Max(0.2, departureDistanceAu), 2);
            request.MeanSolarFluxWm2 = 1361.0 / Math.Pow(Math.Max(0.2, meanDistanceAu), 2);
            request.PhaseAngleDeg = transfer.PhaseAngle * Core.LambertSolver.Rad2Deg;
            request.TransferAngleDeg = transfer.TransferAngle * Core.LambertSolver.Rad2Deg;
            request.LongWay = transfer.LongWay;
            request.CorrectionReserveDvMps = Math.Clamp(transfer.DVTotal * 0.03, 25.0, 300.0);
        }
        else
        {
            request.DvTotalMps = 10000.0;
        }

        return request;
    }

    private Core.ExecutionResult? ResolveRocketBuilderExecutionResult()
    {
        if (SelectedKey(_mode, "single") != "single")
        {
            return _lastExecutionResult;
        }

        try
        {
            var scenario = BuildScenarioFromForm();
            scenario.Request.CsvOutputPath = null;
            scenario.Request.ResultOutputPath = null;
            var seedPath = _currentScenarioPath ?? Path.Combine(AppContext.BaseDirectory, "rocket-builder-seed.json");
            return Core.ScenarioExecutor.Execute(scenario, seedPath, Core.ScenarioJson.Options);
        }
        catch
        {
            return _lastExecutionResult;
        }
    }

    private void LoadScenarioIntoForm(Core.ScenarioInput scenario, string? filePath)
    {
        _suspendRefresh = true;
        try
        {
            _currentScenarioPath = filePath;
            _currentFile.Text = filePath ?? string.Empty;
            SelectPlanet(_origin, scenario.Request.Origin);
            SelectPlanet(_destination, scenario.Request.Destination);
            SelectChoice(_mode, (scenario.Request.Mode ?? "single").Trim().ToLowerInvariant());
            SelectChoice(_longWay, scenario.Request.LongWay switch { true => "long-way", false => "short-way", _ => "auto" });
            SetPicker(_singleDeparture, scenario.Request.DepartureTime ?? Core.SolarSystemCatalog.ToJ2000Seconds(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)));
            _singleTravelDays.Value = Clamp((scenario.Request.TravelTime ?? 220 * Core.SolarSystemCatalog.SecondsPerDay) / Core.SolarSystemCatalog.SecondsPerDay, _singleTravelDays);
            SetPicker(_windowStart, scenario.Request.DepartureWindowStart ?? Core.SolarSystemCatalog.ToJ2000Seconds(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
            SetPicker(_windowEnd, scenario.Request.DepartureWindowEnd ?? Core.SolarSystemCatalog.ToJ2000Seconds(new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)));
            _travelMinDays.Value = Clamp((scenario.Request.TravelTimeMin ?? 120 * Core.SolarSystemCatalog.SecondsPerDay) / Core.SolarSystemCatalog.SecondsPerDay, _travelMinDays);
            _travelMaxDays.Value = Clamp((scenario.Request.TravelTimeMax ?? 360 * Core.SolarSystemCatalog.SecondsPerDay) / Core.SolarSystemCatalog.SecondsPerDay, _travelMaxDays);
            _departureSteps.Value = Clamp(scenario.Request.DepartureSteps, _departureSteps);
            _travelSteps.Value = Clamp(scenario.Request.TravelTimeSteps, _travelSteps);
            _departureOrbitPeriapsisKm.Value = Clamp(scenario.Request.ResolveDepartureOrbitPeriapsisAltitude() / 1000.0, _departureOrbitPeriapsisKm);
            _departureOrbitApoapsisKm.Value = Clamp(scenario.Request.ResolveDepartureOrbitApoapsisAltitude() / 1000.0, _departureOrbitApoapsisKm);
            SetArrivalMode(scenario.Request.ArrivalParkingOrbitAltitude, scenario.Request.ArrivalManeuverMode);
            _arrivalAerobraking.Checked = scenario.Request.UseAerobraking;
            _csvPath.Text = scenario.Request.CsvOutputPath ?? "data/results/earth-mars-porkchop.csv";
            _resultPath.Text = scenario.Request.ResultOutputPath ?? "data/results/result.json";
            
            // Load launch configuration
            if (scenario.Request.Launch is { } launch)
            {
                SelectChoice(_launchEnabled, launch.Enabled ? "surface" : "orbit");
                SelectChoice(_launchMode, launch.Mode ?? "quick");
                _launchMassKg.Value = Clamp(launch.RocketInitialMass, _launchMassKg);
                _launchThrustMn.Value = Clamp(launch.RocketThrust / 1e6, _launchThrustMn);
                _launchIspSec.Value = Clamp(launch.RocketIsp, _launchIspSec);
                _launchLatitude.Value = Clamp(launch.LaunchLatitude, _launchLatitude);
                _launchLongitude.Value = Clamp(launch.LaunchLongitude, _launchLongitude);
                _launchInclination.Value = Clamp(launch.TargetInclination, _launchInclination);
            }
            else
            {
                SelectChoice(_launchEnabled, "orbit");
            }
            
            _summary.Clear();
            _resultJson.Clear();
            _writtenFiles.Clear();
            _lastExecutionResult = null;
            ClearVisualizationResult();
        }
        finally { _suspendRefresh = false; }
        RefreshAll();
        SetStatus(T("status.scenario_loaded"));
    }

    private Core.ScenarioInput BuildScenarioFromForm()
    {
        var origin = PlanetName(_origin, T("field.origin_planet"));
        var destination = PlanetName(_destination, T("field.destination_planet"));
        if (origin.Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(T("error.same_planets"));

        var departurePeriapsisAltitude = (double)_departureOrbitPeriapsisKm.Value * 1000.0;
        var departureApoapsisAltitude = (double)_departureOrbitApoapsisKm.Value * 1000.0;
        if (departureApoapsisAltitude < departurePeriapsisAltitude)
        {
            throw new InvalidOperationException(T("error.departure_orbit_order"));
        }

        var mode = SelectedKey(_mode, "single");
        var request = new Core.CalculationRequest
        {
            Mode = mode,
            Origin = origin,
            Destination = destination,
            DepartureParkingOrbitAltitude = departurePeriapsisAltitude,
            DepartureParkingOrbitPeriapsisAltitude = departurePeriapsisAltitude,
            DepartureParkingOrbitApoapsisAltitude = departureApoapsisAltitude,
            ArrivalParkingOrbitAltitude = ResolveArrivalAltitude(),
            ArrivalManeuverMode = ResolveArrivalManeuverMode(),
            UseAerobraking = ResolveUseAerobraking(),
            LongWay = SelectedKey(_longWay, "auto") switch { "short-way" => false, "long-way" => true, _ => null },
            CsvOutputPath = mode == "porkchop" ? EmptyToNull(_csvPath.Text) : null,
            ResultOutputPath = EmptyToNull(_resultPath.Text),
            Launch = new Core.LaunchConfiguration
            {
                Enabled = SelectedKey(_launchEnabled, "orbit") == "surface",
                Mode = SelectedKey(_launchMode, "quick"),
                RocketInitialMass = (double)_launchMassKg.Value,
                RocketThrust = (double)_launchThrustMn.Value * 1e6,
                RocketIsp = (double)_launchIspSec.Value,
                LaunchLatitude = (double)_launchLatitude.Value,
                LaunchLongitude = (double)_launchLongitude.Value,
                TargetInclination = (double)_launchInclination.Value
            }
        };

        if (mode == "single")
        {
            request.DepartureTime = Core.SolarSystemCatalog.ToJ2000Seconds(PickerUtc(_singleDeparture));
            request.TravelTime = (double)_singleTravelDays.Value * Core.SolarSystemCatalog.SecondsPerDay;
        }
        else
        {
            request.DepartureWindowStart = Core.SolarSystemCatalog.ToJ2000Seconds(PickerUtc(_windowStart));
            request.DepartureWindowEnd = Core.SolarSystemCatalog.ToJ2000Seconds(PickerUtc(_windowEnd));
            request.TravelTimeMin = (double)_travelMinDays.Value * Core.SolarSystemCatalog.SecondsPerDay;
            request.TravelTimeMax = (double)_travelMaxDays.Value * Core.SolarSystemCatalog.SecondsPerDay;
            request.DepartureSteps = (int)_departureSteps.Value;
            request.TravelTimeSteps = (int)_travelSteps.Value;
        }

        return new Core.ScenarioInput
        {
            Calendar = Core.SolarSystemCatalog.CreateUtcCalendarInput(),
            CentralBody = Core.SolarSystemCatalog.CreateSunInput(),
            Bodies = [Core.SolarSystemCatalog.CreateBodyInput(origin), Core.SolarSystemCatalog.CreateBodyInput(destination)],
            Request = request
        };
    }

    private void RefreshAll()
    {
        if (_suspendRefresh) return;
        RefreshModeState();
        UpdatePlanetDetails();
        RefreshPreview();
    }

    private void RefreshModeState()
    {
        var single = SelectedKey(_mode, "single") == "single";
        _singleDeparture.Enabled = single;
        _singleTravelDays.Enabled = single;
        _windowStart.Enabled = !single;
        _windowEnd.Enabled = !single;
        _travelMinDays.Enabled = !single;
        _travelMaxDays.Enabled = !single;
        _departureSteps.Enabled = !single;
        _travelSteps.Enabled = !single;
        var arrivalMode = SelectedKey(_arrivalMode, "circular orbit");
        _arrivalOrbitKm.Enabled = arrivalMode is "elliptic capture" or "circular orbit";
        var aerobrakingAvailable = SupportsAerobrakingSelection();
        var arrivalBurnEnabled = arrivalMode is "elliptic capture" or "circular orbit";
        _arrivalAerobraking.Enabled = aerobrakingAvailable && arrivalBurnEnabled;
        _arrivalAerobraking.Visible = aerobrakingAvailable;
        if (!aerobrakingAvailable || !arrivalBurnEnabled)
        {
            _arrivalAerobraking.Checked = false;
        }
        
        // Enable/disable rocket parameters based on launch mode
        var launchEnabled = SelectedKey(_launchEnabled, "orbit") == "surface";
        var launchModeDetailed = SelectedKey(_launchMode, "quick") == "detailed";
        _launchMode.Enabled = launchEnabled;
        _launchLatitude.Enabled = launchEnabled;
        _launchLongitude.Enabled = launchEnabled;
        _launchInclination.Enabled = launchEnabled;
        _launchMassKg.Enabled = launchEnabled && launchModeDetailed;
        _launchThrustMn.Enabled = launchEnabled && launchModeDetailed;
        _launchIspSec.Enabled = launchEnabled && launchModeDetailed;
    }

    private void UpdatePlanetDetails()
    {
        _originDetails.Text = PlanetSummary(_origin);
        _destinationDetails.Text = PlanetSummary(_destination);
    }

    private void RefreshPreview()
    {
        if (_suspendRefresh) return;
        try { _scenarioJson.Text = Core.ScenarioJson.Serialize(BuildScenarioFromForm()); }
        catch (Exception ex) { _scenarioJson.Text = $"{T("text.validation_error")}{Environment.NewLine}{ex.Message}"; }
    }

    private string PlanetSummary(ComboBox combo)
    {
        if (combo.SelectedItem is null) return T("text.select_planet");
        var body = Core.SolarSystemCatalog.CreateBodyInput(combo.SelectedItem.ToString()!);
        return string.Join(Environment.NewLine, new[]
        {
            body.Name,
            body.Name == "Earth" ? T("text.planet_series_earth") : T("text.planet_series_other"),
            "",
            $"{T("text.planet.mu")}: {body.GravitationalParameter:G17} m^3/s^2",
            $"{T("text.planet.radius")}: {body.Radius / 1000.0:0.###} km",
            $"{T("text.planet.soi")}: {body.SphereOfInfluence.GetValueOrDefault() / 1000.0:0.###} km",
            $"{T("text.planet.a")}: {body.Orbit.SemiMajorAxis / Core.SolarSystemCatalog.AstronomicalUnit:0.000000000} au",
            $"{T("text.planet.e")}: {body.Orbit.Eccentricity:0.000000000}",
            $"{T("text.planet.i")}: {body.Orbit.InclinationDeg:0.000000000} deg",
            $"{T("text.planet.lan")}: {body.Orbit.LongitudeOfAscendingNodeDeg:0.000000000} deg",
            $"{T("text.planet.arg_peri")}: {body.Orbit.ArgumentOfPeriapsisDeg:0.000000000} deg",
            $"{T("text.planet.mj2000")}: {body.Orbit.MeanAnomalyAtEpochDeg:0.000000000} deg"
        });
    }

    private string TryLoadResultJson(string baseScenarioPath, Core.ScenarioInput scenario, IReadOnlyList<string> writtenFiles)
    {
        var configured = EmptyToNull(scenario.Request.ResultOutputPath);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolved = ResolveOutput(baseScenarioPath, configured);
            if (File.Exists(resolved)) return File.ReadAllText(resolved);
        }
        var discovered = writtenFiles.FirstOrDefault(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(discovered) && File.Exists(discovered) ? File.ReadAllText(discovered) : T("text.no_result_json");
    }

    private void ApplyVisualizationResult(Core.ExecutionResult result)
    {
        _deltaVVisual.SetResult(result.Transfer, result.Porkchop, result.Calendar);
        _solarSystemView.SetResult(result.Transfer, result.Calendar);
    }

    private void ClearVisualizationResult()
    {
        _deltaVVisual.SetResult(null, null, null);
        _solarSystemView.SetResult(null, null);
    }

    private Control InfoCard()
    {
        var panel = new Panel
        {
            Tag = "info-card",
            Padding = new Padding(16),
            BackColor = UiTheme.CardBackground,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 14),
            MinimumSize = new Size(LeftPanelMinWidth, 0)
        };

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = T("info.title"),
            Margin = new Padding(0, 0, 0, 6),
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = T("info.body"),
            Margin = Padding.Empty,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.TextSecondary
        }, 0, 1);

        _infoCardLayout = layout;
        panel.Controls.Add(layout);
        return panel;
    }

    private GroupBox Group(string title, params (string Label, Control Control)[] rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = rows.Length
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = rows[i].Label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 7, 8, 8), MinimumSize = new Size(0, 24) }, 0, i);
            rows[i].Control.Dock = DockStyle.Fill;
            rows[i].Control.Margin = new Padding(0, 2, 0, 8);
            table.Controls.Add(rows[i].Control, 1, i);
        }

        var group = new ThemedGroupBox
        {
            Text = title,
            MinimumSize = new Size(LeftPanelMinWidth, 0),
            Padding = new Padding(14, 30, 14, 14),
            Margin = new Padding(0, 0, 0, 14),
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
        };
        group.Controls.Add(table);
        return group;
    }

    private GroupBox PlanetDetailsGroup()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var originGroup = new ThemedGroupBox { Text = T("group.origin_planet"), Dock = DockStyle.Fill, Padding = new Padding(12, 28, 12, 12), Margin = new Padding(0, 0, 10, 0) };
        _originDetails.Dock = DockStyle.Fill;
        originGroup.Controls.Add(_originDetails);
        var destinationGroup = new ThemedGroupBox { Text = T("group.destination_planet"), Dock = DockStyle.Fill, Padding = new Padding(12, 28, 12, 12) };
        _destinationDetails.Dock = DockStyle.Fill;
        destinationGroup.Controls.Add(_destinationDetails);
        layout.Controls.Add(originGroup, 0, 0);
        layout.Controls.Add(destinationGroup, 1, 0);

        var group = new ThemedGroupBox
        {
            Text = T("group.selected_planet_data"),
            MinimumSize = new Size(LeftPanelMinWidth, 0),
            Padding = new Padding(14, 30, 14, 14),
            Margin = new Padding(0, 0, 0, 14),
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
        };
        group.Controls.Add(layout);
        return group;
    }

    private static void AddLeftSection(TableLayoutPanel layout, Control control)
    {
        var rowIndex = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 0, 0, 14);
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        layout.Controls.Add(control, 0, rowIndex);
    }

    private static TabPage Tab(string title, Control control)
    {
        var page = new TabPage(title);
        page.Controls.Add(control);
        return page;
    }

    private void SelectPlanet(ComboBox combo, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { combo.SelectedIndex = 0; return; }
        foreach (var item in combo.Items.Cast<object>())
        {
            if (string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        throw new InvalidOperationException(F("error.planet_not_found", value));
    }

    private void SetArrivalMode(double? altitudeMeters, string? maneuverMode)
    {
        if (!altitudeMeters.HasValue)
        {
            SelectChoice(_arrivalMode, "ignore arrival burn");
            return;
        }
        if (altitudeMeters.Value <= 0)
        {
            SelectChoice(_arrivalMode, "flyby");
            return;
        }

        var modeKey = maneuverMode?.Trim().ToLowerInvariant() switch
        {
            "circular-capture" => "circular orbit",
            "capture orbit" => "circular orbit",
            _ => "circular orbit"
        };
        SelectChoice(_arrivalMode, modeKey);
        _arrivalOrbitKm.Value = Clamp(altitudeMeters.Value / 1000.0, _arrivalOrbitKm);
    }

    private double? ResolveArrivalAltitude()
    {
        return SelectedKey(_arrivalMode, "circular orbit") switch
        {
            "ignore arrival burn" => null,
            "flyby" => 0.0,
            _ => (double)_arrivalOrbitKm.Value * 1000.0
        };
    }

    private string? ResolveArrivalManeuverMode()
    {
        return SelectedKey(_arrivalMode, "circular orbit") switch
        {
            "ignore arrival burn" => null,
            "flyby" => null,
            "circular orbit" => "circular-capture",
            _ => "elliptic-capture"
        };
    }

    private bool ResolveUseAerobraking()
    {
        var arrivalAltitude = ResolveArrivalAltitude();
        return SupportsAerobrakingSelection()
               && _arrivalAerobraking.Checked
               && arrivalAltitude.HasValue
               && arrivalAltitude.Value > 0.0;
    }

    private bool SupportsAerobrakingSelection()
    {
        var destination = _destination.SelectedItem?.ToString();
        return string.Equals(destination, "Earth", StringComparison.OrdinalIgnoreCase)
               || string.Equals(destination, "Venus", StringComparison.OrdinalIgnoreCase);
    }

    private void ChangeLanguage()
    {
        if (_suspendRefresh) return;
        var language = SelectedKey(_language, "en") == "ru" ? UiLanguage.Russian : UiLanguage.English;
        if (language == _uiLanguage) return;

        _suspendRefresh = true;
        try
        {
            _uiLanguage = language;
            BuildUi();
            FitWindowToScreen();
        }
        finally
        {
            _suspendRefresh = false;
        }

        RefreshAll();
    }

    private void PopulatePlanetChoices()
    {
        if (_origin.Items.Count > 0 && _destination.Items.Count > 0) return;
        foreach (var planet in Core.SolarSystemCatalog.PlanetNames)
        {
            _origin.Items.Add(planet);
            _destination.Items.Add(planet);
        }
    }

    private void ApplyLocalizedChoices()
    {
        SetChoiceItems(_language, _uiLanguage == UiLanguage.Russian ? "ru" : "en",
            new ChoiceItem("en", "English"),
            new ChoiceItem("ru", "Русский"));
        SetChoiceItems(_mode, SelectedKey(_mode, "single"),
            new ChoiceItem("single", T("option.mode.single")),
            new ChoiceItem("porkchop", T("option.mode.porkchop")));
        SetChoiceItems(_longWay, SelectedKey(_longWay, "auto"),
            new ChoiceItem("auto", T("option.long_way.auto")),
            new ChoiceItem("short-way", T("option.long_way.short")),
            new ChoiceItem("long-way", T("option.long_way.long")));
        SetChoiceItems(_arrivalMode, SelectedKey(_arrivalMode, "circular orbit"),
            new ChoiceItem("elliptic capture", T("option.arrival.elliptic")),
            new ChoiceItem("circular orbit", T("option.arrival.circular")),
            new ChoiceItem("flyby", T("option.arrival.flyby")),
            new ChoiceItem("ignore arrival burn", T("option.arrival.ignore")));
        SetChoiceItems(_launchEnabled, SelectedKey(_launchEnabled, "orbit"),
            new ChoiceItem("orbit", T("option.launch_profile.orbit")),
            new ChoiceItem("surface", T("option.launch_profile.surface")));
        SetChoiceItems(_launchMode, SelectedKey(_launchMode, "quick"),
            new ChoiceItem("quick", T("option.launch_mode.quick")),
            new ChoiceItem("detailed", T("option.launch_mode.detailed")));
    }

    private static void SetChoiceItems(ComboBox combo, string selectedKey, params ChoiceItem[] items)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.AddRange(items);
        combo.EndUpdate();
        SelectChoice(combo, selectedKey);
    }

    private static void SelectChoice(ComboBox combo, string key)
    {
        foreach (var item in combo.Items.OfType<ChoiceItem>())
        {
            if (item.Key == key)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static string SelectedKey(ComboBox combo, string fallback)
    {
        return combo.SelectedItem is ChoiceItem item ? item.Key : fallback;
    }

    private void UpdateLeftScrollLayout()
    {
        if (_leftScrollHost is null || _leftContent is null)
        {
            return;
        }

        var availableWidth = Math.Max(LeftPanelMinWidth, _leftScrollHost.ClientSize.Width - 4);
        _leftContent.SuspendLayout();
        try
        {
            _leftContent.MaximumSize = new Size(availableWidth, 0);
            _leftContent.MinimumSize = new Size(availableWidth, 0);
            _leftContent.Location = new Point(0, 0);

            if (_infoCardLayout?.Parent is Panel infoCard)
            {
                var cardWidth = Math.Max(0, availableWidth - infoCard.Padding.Horizontal - 2);
                _infoCardLayout.MaximumSize = new Size(cardWidth, 0);
                foreach (var label in _infoCardLayout.Controls.OfType<Label>())
                {
                    label.MaximumSize = new Size(cardWidth, 0);
                }

                var cardContent = _infoCardLayout.GetPreferredSize(new Size(cardWidth, 0));
                infoCard.Width = availableWidth;
                infoCard.Height = cardContent.Height + infoCard.Padding.Vertical + 2;
            }

            foreach (Control section in _leftContent.Controls)
            {
                section.Width = availableWidth;

                if (section is GroupBox group)
                {
                    var sectionPreferred = group.GetPreferredSize(new Size(availableWidth, 0));
                    group.Height = Math.Max(section.PreferredSize.Height, sectionPreferred.Height);
                }
            }

            var preferred = _leftContent.GetPreferredSize(new Size(availableWidth, 0));
            _leftContent.Size = new Size(availableWidth, preferred.Height);
            _leftScrollHost.AutoScrollMinSize = new Size(availableWidth, preferred.Height + 12);
        }
        finally
        {
            _leftContent.ResumeLayout();
        }
    }

    private void FitWindowToScreen()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var targetWidth = Math.Max(MinimumWindowSize.Width, Math.Min(PreferredWindowSize.Width, workingArea.Width - 24));
        var targetHeight = Math.Max(MinimumWindowSize.Height, Math.Min(PreferredWindowSize.Height, workingArea.Height - 24));

        Size = new Size(targetWidth, targetHeight);

        if (Width >= workingArea.Width - 24 || Height >= workingArea.Height - 24)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private string RocketBuilderActionText() => _uiLanguage == UiLanguage.Russian ? "AI-конструктор ракеты" : "Rocket Builder AI";

    private string T(string key) => UiTextCatalog.Get(_uiLanguage, key);
    private string F(string key, params object[] args) => UiTextCatalog.Format(_uiLanguage, key, args);
    private static string ResolveOutput(string inputPath, string outputPath) => Path.IsPathRooted(outputPath) ? outputPath : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory(), outputPath));
    private string PlanetName(ComboBox combo, string name) => combo.SelectedItem?.ToString() ?? throw new InvalidOperationException(F("error.required", name));
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTimeOffset PickerUtc(DateTimePicker picker) => new(DateTime.SpecifyKind(picker.Value, DateTimeKind.Utc));
    private static void SetPicker(DateTimePicker picker, double seconds) => picker.Value = Core.SolarSystemCatalog.FromJ2000Seconds(seconds).UtcDateTime;
    private static decimal Clamp(double value, NumericUpDown control) => Math.Min(control.Maximum, Math.Max(control.Minimum, (decimal)value));
    private static Button Action(string text, EventHandler onClick, Color? color = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(126, 36),
            Height = 36,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 8, 0)
        };
        UiTheme.StyleActionButton(button, color);
        button.Click += onClick;
        return button;
    }

    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F), BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private static DateTimePicker UtcPicker() => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss 'UTC'", Font = new Font("Segoe UI", 10F), MinDate = new DateTime(1900, 1, 1), MaxDate = new DateTime(2100, 12, 31), Value = new DateTime(2026, 11, 12, 0, 0, 0), CalendarMonthBackground = UiTheme.InputBackground, CalendarForeColor = UiTheme.TextPrimary, CalendarTitleBackColor = UiTheme.CardAltBackground, CalendarTitleForeColor = UiTheme.TextPrimary };
    private static NumericUpDown Num(decimal value, decimal min, decimal max, int decimals) => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, ThousandsSeparator = true, Font = new Font("Segoe UI", 10F), BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private static TextBox Box(string? placeholder = null) => new() { PlaceholderText = placeholder ?? string.Empty, Font = new Font("Segoe UI", 10F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox ReadOnlyLine() => new() { ReadOnly = true, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.CardAltBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox ReadOnlyArea() => new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.25F), BorderStyle = BorderStyle.FixedSingle, Width = 290, Height = 220, BackColor = UiTheme.CardAltBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox CodeBox() => new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private static RichTextBox SummaryBox() => new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.75F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private void SetStatus(string text) => _status.Text = text;
    private void Error(string message) { SetStatus(F("status.error", message)); MessageBox.Show(this, message, T("app.short_title"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
}
