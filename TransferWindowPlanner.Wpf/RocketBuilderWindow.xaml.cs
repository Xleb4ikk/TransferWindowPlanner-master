using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using Core = TrajectoryCalculator;

namespace TrajectoryCalculator.Gui;

public partial class RocketBuilderWindow : Window
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RocketBuilderService _service = new();
    private readonly UiLanguage _language = UiTextCatalog.DetectInitialLanguage();
    private bool _suspendPreview = true;

    public RocketBuilderWindow(RocketBuilderMissionRequest? seed = null)
    {
        InitializeComponent();
        PopulateChoices();
        ApplyDefaults(seed);
        DefaultModelLabel.Text = System.IO.Path.GetFileName(_service.DefaultModelDirectory);
    }

    private string T(string key) => UiTextCatalog.Get(_language, key);

    private void PopulateChoices()
    {
        PopulateModels();
        SetItems(DeviceCombo,
            ("auto", L("auto", "авто")),
            ("cuda", "cuda"),
            ("cpu", "cpu"));
        SetItems(LongWayCombo,
            ("auto", L("auto", "авто")),
            ("short", L("short-way", "короткий путь")),
            ("long", L("long-way", "длинный путь")));
        PopulatePlanets(OriginCombo);
        PopulatePlanets(DestCombo);
    }

    private void PopulateModels()
    {
        ModelCombo.Items.Clear();
        foreach (var m in _service.GetAvailableModels())
            ModelCombo.Items.Add(m);
        if (ModelCombo.Items.Count > 0)
        {
            for (int i = 0; i < ModelCombo.Items.Count; i++)
                if (((RocketBuilderModelOption)ModelCombo.Items[i]).DirectoryPath.Equals(_service.DefaultModelDirectory, StringComparison.OrdinalIgnoreCase))
                { ModelCombo.SelectedIndex = i; return; }
            ModelCombo.SelectedIndex = 0;
        }
    }

    private void PopulatePlanets(ComboBox combo)
    {
        combo.Items.Clear();
        combo.Items.Add(new ChoiceItem("", L("(optional)", "(необязательно)")));
        foreach (var p in Core.SolarSystemCatalog.PlanetNames)
            combo.Items.Add(new ChoiceItem(p, p));
        combo.SelectedIndex = 0;
    }

    private void ApplyDefaults(RocketBuilderMissionRequest? seed)
    {
        _suspendPreview = true;
        TopKBox.Text = "5";
        PayloadBox.Text = "1000";
        TravelDaysBox.Text = "250";
        MeanDistBox.Text = "1.0";
        MinDistBox.Text = "1.0";
        DvTotalBox.Text = "10000";
        ClearResults();

        if (seed != null)
            LoadRequestIntoForm(seed);

        _suspendPreview = false;
        RefreshRequestPreview();
        StatusText.Text = L("Fill in the mission and run the model.", "Заполни миссию и запусти модель.");
    }

    private void ClearResults()
    {
        BestArchBox.Text = BestStagesBox.Text = BestEffStagesBox.Text = "";
        BestInterpretBox.Text = BestScoreBox.Text = BestMassBox.Text = "";
        BestBoiloffBox.Text = BestDeviceBox.Text = ReasoningBox.Text = "";
        ResponseJsonBox.Text = ConsoleBox.Text = "";
        StagesGrid.ItemsSource = null;
        CandidatesGrid.ItemsSource = null;
    }

    private void LoadRequestIntoForm(RocketBuilderMissionRequest r)
    {
        SelectPlanet(OriginCombo, r.Origin);
        SelectPlanet(DestCombo, r.Destination);
        PayloadBox.Text = r.PayloadMassKg.ToString("F2", CultureInfo.InvariantCulture);
        DvTotalBox.Text = r.DvTotalMps.ToString("F2", CultureInfo.InvariantCulture);
        TravelDaysBox.Text = r.TravelDays.ToString("F3", CultureInfo.InvariantCulture);
        MeanDistBox.Text = r.MeanDistanceAu.ToString("F6", CultureInfo.InvariantCulture);
        MinDistBox.Text = r.MinDistanceAu.ToString("F6", CultureInfo.InvariantCulture);
        SurfaceOrbitBox.Text = r.LaunchAscentDvMps?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        OrbitTmiBox.Text = r.DvEjectionMps.HasValue
            ? Math.Max(0, r.DvEjectionMps.Value - Math.Max(0, r.LaunchAscentDvMps ?? 0)).ToString("F2", CultureInfo.InvariantCulture) : "";
        MoiBox.Text = r.DvInjectionMps?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        CorrectionBox.Text = r.CorrectionReserveDvMps?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        DepOrbitBox.Text = r.DepartureOrbitKm?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        ArrOrbitBox.Text = r.ArrivalOrbitKm?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        DepDistBox.Text = r.DepartureDistanceAu?.ToString("F6", CultureInfo.InvariantCulture) ?? "";
        ArrDistBox.Text = r.ArrivalDistanceAu?.ToString("F6", CultureInfo.InvariantCulture) ?? "";
        DepFluxBox.Text = r.DepartureSolarFluxWm2?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        MeanFluxBox.Text = r.MeanSolarFluxWm2?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        PhaseAngleBox.Text = r.PhaseAngleDeg?.ToString("F3", CultureInfo.InvariantCulture) ?? "";
        TransferAngleBox.Text = r.TransferAngleDeg?.ToString("F3", CultureInfo.InvariantCulture) ?? "";
        SelectChoice(LongWayCombo, r.LongWay switch { true => "long", false => "short", _ => "auto" });
    }

    private RocketBuilderMissionRequest BuildRequest()
    {
        var origin = SelectedPlanet(OriginCombo);
        var dest = SelectedPlanet(DestCombo);
        var surfOrbit = ParseOpt(SurfaceOrbitBox.Text);
        var orbitTmi = ParseOpt(OrbitTmiBox.Text);
        var moi = ParseOpt(MoiBox.Text);
        double? depStack = surfOrbit.HasValue || orbitTmi.HasValue
            ? Math.Max(0, surfOrbit.GetValueOrDefault()) + Math.Max(0, orbitTmi.GetValueOrDefault()) : null;

        if (!string.IsNullOrWhiteSpace(origin) && string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L("Origin and destination must differ.", "Планеты должны отличаться."));

        return new RocketBuilderMissionRequest
        {
            Origin = origin, Destination = dest,
            PayloadMassKg = ParseReq(PayloadBox.Text, "Payload"),
            DvTotalMps = ParseReq(DvTotalBox.Text, "Total dV"),
            TravelDays = ParseReq(TravelDaysBox.Text, "Travel days"),
            MeanDistanceAu = ParseReq(MeanDistBox.Text, "Mean distance"),
            MinDistanceAu = ParseOpt(MinDistBox.Text) ?? ParseReq(MeanDistBox.Text, "Mean distance"),
            LaunchAscentDvMps = surfOrbit,
            DvEjectionMps = depStack,
            DvInjectionMps = moi,
            DepartureOrbitKm = ParseOpt(DepOrbitBox.Text),
            ArrivalOrbitKm = ParseOpt(ArrOrbitBox.Text),
            DepartureDistanceAu = ParseOpt(DepDistBox.Text),
            ArrivalDistanceAu = ParseOpt(ArrDistBox.Text),
            DepartureSolarFluxWm2 = ParseOpt(DepFluxBox.Text),
            MeanSolarFluxWm2 = ParseOpt(MeanFluxBox.Text),
            PhaseAngleDeg = ParseOpt(PhaseAngleBox.Text),
            TransferAngleDeg = ParseOpt(TransferAngleBox.Text),
            CorrectionReserveDvMps = ParseOpt(CorrectionBox.Text),
            SurfaceLaunch = surfOrbit.HasValue && surfOrbit.Value > 0 ? true : null,
            DepartureSurfaceGravityMps2 = RocketBuilderDefaults.ResolveSurfaceGravity(origin),
            LongWay = SelectedKey(LongWayCombo) switch { "short" => false, "long" => true, _ => null }
        };
    }

    private void RefreshRequestPreview()
    {
        if (_suspendPreview) return;
        RefreshDeltaVBudget();
        try { RequestJsonBox.Text = JsonSerializer.Serialize(BuildRequest(), JsonOpts); }
        catch (Exception ex) { RequestJsonBox.Text = ex.Message; }
    }

    private void RefreshDeltaVBudget()
    {
        var surfOrbit = ParseOpt(SurfaceOrbitBox.Text);
        var orbitTmi = ParseOpt(OrbitTmiBox.Text);
        var moi = ParseOpt(MoiBox.Text);
        var correction = ParseOpt(CorrectionBox.Text);
        double? depStack = surfOrbit.HasValue || orbitTmi.HasValue
            ? Math.Max(0, surfOrbit.GetValueOrDefault()) + Math.Max(0, orbitTmi.GetValueOrDefault()) : null;
        double? brTotal = depStack.HasValue || moi.HasValue
            ? Math.Max(0, depStack.GetValueOrDefault()) + Math.Max(0, moi.GetValueOrDefault()) : null;
        var alloc = (brTotal ?? ParseReq(DvTotalBox.Text, "Total dV")) + Math.Max(0, correction.GetValueOrDefault());
        DepartureStackBox.Text = depStack?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        BreakdownTotalBox.Text = brTotal?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
        AllocationTotalBox.Text = alloc.ToString("F2", CultureInfo.InvariantCulture);
    }

    private async void OnPredict(object sender, RoutedEventArgs e)
    {
        RocketBuilderMissionRequest request;
        try { request = BuildRequest(); }
        catch (Exception ex) { ShowError(ex.Message); return; }

        var modelDir = ModelCombo.SelectedItem is RocketBuilderModelOption m ? m.DirectoryPath : "";
        if (string.IsNullOrWhiteSpace(modelDir))
        { ShowError(L("Choose a model first.", "Выбери модель.")); return; }

        var device = SelectedKey(DeviceCombo);
        int.TryParse(TopKBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var topK);
        if (topK < 1) topK = 5;

        SetBusy(true);
        StatusText.Text = L("Running prediction...", "Запускаю предсказание...");

        try
        {
            var exec = await _service.PredictAsync(request, modelDir, "python", device, topK);
            ApplyPrediction(exec);
            StatusText.Text = L("Prediction completed.", "Предсказание завершено.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private void ApplyPrediction(RocketBuilderPredictionExecution exec)
    {
        var b = exec.Payload.Best;
        BestArchBox.Text = b.ArchitectureKey;
        BestStagesBox.Text = b.StageCount.ToString(CultureInfo.InvariantCulture);
        BestEffStagesBox.Text = b.EffectiveStageCount.ToString(CultureInfo.InvariantCulture);
        BestInterpretBox.Text = FormatInterpretation(b);
        BestScoreBox.Text = FormatMass(b.PredictedScoreKgEquivalent);
        BestMassBox.Text = FormatMass(b.PredictedLaunchMassKg);
        BestBoiloffBox.Text = FormatMass(b.PredictedBoiloffMassKg);
        BestDeviceBox.Text = exec.Payload.Device;
        ReasoningBox.Text = exec.Payload.Reasoning.Count == 0
            ? L("No reasoning returned.", "Модель не вернула пояснение.")
            : string.Join(Environment.NewLine, exec.Payload.Reasoning.Select(l => "- " + l));
        RequestJsonBox.Text = exec.RequestJson;
        ResponseJsonBox.Text = exec.ResponseJson;
        ConsoleBox.Text = $"model:  {exec.ModelDirectory}\ncwd:    {exec.WorkingDirectory}\n"
            + (string.IsNullOrWhiteSpace(exec.StandardOutput) ? "" : $"\nstdout:\n{exec.StandardOutput.Trim()}\n")
            + (string.IsNullOrWhiteSpace(exec.StandardError) ? "" : $"\nstderr:\n{exec.StandardError.Trim()}\n");

        StagesGrid.ItemsSource = b.Stages.Select(s => new
        {
            Stage = s.StageNumber,
            Kind = LocalizeKind(s.StageKind),
            Role = s.Role, Segment = s.Segment,
            Fuel = s.PropellantLabel, Engine = s.EngineLabel,
            Engines = s.EngineCount,
            ThrustKn = s.AvailableThrustKn,
            IspS = s.EngineVacuumIspSeconds,
            TankKg = s.TankMassKg,
            DvMps = s.DvMps,
            DvPct = s.DvShare * 100
        }).ToList();

        CandidatesGrid.ItemsSource = exec.Payload.TopCandidates
            .Select((c, i) => new
            {
                Rank = i + 1, Architecture = c.ArchitectureKey,
                Stages = FormatInterpretation(c),
                EffectiveStages = c.EffectiveStageCount,
                Score = c.PredictedScoreKgEquivalent,
                EngineeringScore = c.EstimatedTotalScoreKgEquivalent,
                LaunchMass = c.PredictedLaunchMassKg,
                Boiloff = c.PredictedBoiloffMassKg
            }).ToList();
    }

    private void OnLoadJson(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = L("Open request JSON", "Открыть JSON запроса")
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var r = JsonSerializer.Deserialize<RocketBuilderMissionRequest>(
                File.ReadAllText(dialog.FileName), JsonOpts)
                ?? throw new InvalidOperationException(L("Failed to parse JSON.", "Не удалось разобрать JSON."));
            LoadRequestIntoForm(r);
            RefreshRequestPreview();
            StatusText.Text = L($"Loaded: {dialog.FileName}", $"Загружено: {dialog.FileName}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OnSaveJson(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = L("Save request JSON", "Сохранить JSON запроса"),
            FileName = "rocket-builder-request.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var json = JsonSerializer.Serialize(BuildRequest(), JsonOpts);
            File.WriteAllText(dialog.FileName, json + "\n");
            StatusText.Text = L($"Saved: {dialog.FileName}", $"Сохранено: {dialog.FileName}");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OnFieldChanged(object sender, EventArgs e) => RefreshRequestPreview();

    private void SetBusy(bool busy) { IsEnabled = !busy; Cursor = busy ? System.Windows.Input.Cursors.Wait : null; }

    private void ShowError(string msg)
    {
        StatusText.Text = msg;
        MessageBox.Show(this, msg, Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string FormatInterpretation(RocketBuilderCandidatePrediction c)
    {
        if (c.ServiceStageCount > 0)
            return L($"{c.LaunchStageCount} launch + {c.ServiceStageCount} service",
                     $"{c.LaunchStageCount} старт. + {c.ServiceStageCount} сервис.");
        return c.EffectiveStageCount == c.StageCount
            ? c.StageCount.ToString(CultureInfo.InvariantCulture)
            : $"{c.EffectiveStageCount}/{c.StageCount}";
    }

    private string LocalizeKind(string kind) => kind switch
    {
        "launch" => L("launch", "стартовая"),
        "service" => L("service", "сервисная"),
        "arrival" => L("arrival", "прибытие"),
        _ => kind
    };

    private static string FormatMass(double v) => v.ToString("N0", CultureInfo.InvariantCulture);

    private static double ParseReq(string text, string field)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var v))
            throw new InvalidOperationException($"{field}: expected a number.");
        return v;
    }

    private static double? ParseOpt(string text)
    {
        var t = text.Trim();
        if (string.IsNullOrWhiteSpace(t)) return null;
        return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static void SelectPlanet(ComboBox combo, string? value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is ChoiceItem ci && string.Equals(ci.Key, value ?? "", StringComparison.OrdinalIgnoreCase))
            { combo.SelectedIndex = i; return; }
        combo.SelectedIndex = 0;
    }

    private static string? SelectedPlanet(ComboBox combo) =>
        combo.SelectedItem is ChoiceItem ci && !string.IsNullOrWhiteSpace(ci.Key) ? ci.Key : null;

    private static string SelectedKey(ComboBox combo) =>
        combo.SelectedItem is ChoiceItem ci ? ci.Key : "auto";

    private static void SetItems(ComboBox combo, params (string Key, string Label)[] items)
    {
        combo.Items.Clear();
        foreach (var (k, l) in items) combo.Items.Add(new ChoiceItem(k, l));
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static void SelectChoice(ComboBox combo, string key)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is ChoiceItem ci && ci.Key == key)
            { combo.SelectedIndex = i; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private record ChoiceItem(string Key, string Label) { public override string ToString() => Label; }

    private string L(string en, string ru) => _language == UiLanguage.Russian ? ru : en;
}
