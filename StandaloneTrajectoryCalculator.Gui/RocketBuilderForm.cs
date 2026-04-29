using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core = StandaloneTrajectoryCalculator;

namespace StandaloneTrajectoryCalculator.Gui;

internal sealed class RocketBuilderForm : Form
{
    private sealed record ChoiceItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RocketBuilderPredictorService _predictorService;
    private readonly UiLanguage _language;

    private readonly ComboBox _modelSelector = DropDown();
    private readonly ComboBox _device = DropDown();
    private readonly NumericUpDown _topK = Num(5, 1, 20, 0);

    private readonly ComboBox _origin = DropDown();
    private readonly ComboBox _destination = DropDown();
    private readonly NumericUpDown _payloadMassKg = Num(1000, 1, 1000000000, 2);
    private readonly NumericUpDown _dvTotalMps = Num(10000, 1, 1000000, 2);
    private readonly NumericUpDown _travelDays = Num(250, 1, 10000, 3);
    private readonly NumericUpDown _meanDistanceAu = Num(1, 0.2m, 100, 6);
    private readonly NumericUpDown _minDistanceAu = Num(1, 0.2m, 100, 6);

    private readonly TextBox _surfaceToOrbitDvMps = OptionalNumberBox();
    private readonly TextBox _orbitToTmiDvMps = OptionalNumberBox();
    private readonly TextBox _moiDvMps = OptionalNumberBox();
    private readonly TextBox _departureOrbitKm = OptionalNumberBox();
    private readonly TextBox _arrivalOrbitKm = OptionalNumberBox();
    private readonly TextBox _departureDistanceAu = OptionalNumberBox();
    private readonly TextBox _arrivalDistanceAu = OptionalNumberBox();
    private readonly TextBox _departureFlux = OptionalNumberBox();
    private readonly TextBox _meanFlux = OptionalNumberBox();
    private readonly TextBox _phaseAngleDeg = OptionalNumberBox();
    private readonly TextBox _transferAngleDeg = OptionalNumberBox();
    private readonly TextBox _correctionReserveDv = OptionalNumberBox();
    private readonly TextBox _departureStackSummary = ReadOnlyLine();
    private readonly TextBox _breakdownTotalSummary = ReadOnlyLine();
    private readonly TextBox _allocationTotalSummary = ReadOnlyLine();
    private readonly ComboBox _longWay = DropDown();

    private readonly TextBox _bestArchitecture = ReadOnlyLine();
    private readonly TextBox _bestStageCount = ReadOnlyLine();
    private readonly TextBox _bestEffectiveStageCount = ReadOnlyLine();
    private readonly TextBox _bestStageInterpretation = ReadOnlyLine();
    private readonly TextBox _bestScore = ReadOnlyLine();
    private readonly TextBox _bestLaunchMass = ReadOnlyLine();
    private readonly TextBox _bestBoiloff = ReadOnlyLine();
    private readonly TextBox _bestDevice = ReadOnlyLine();
    private readonly TextBox _reasoning = ReadOnlyArea();
    private readonly TextBox _requestJson = CodeBox();
    private readonly TextBox _responseJson = CodeBox();
    private readonly TextBox _consoleLog = CodeBox();
    private readonly DataGridView _stagesGrid = BuildStagesGrid();
    private readonly DataGridView _candidatesGrid = BuildCandidatesGrid();
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) };

    private bool _suspendPreview;
    private bool? _surfaceLaunch;
    private double? _launchAscentDvMps;
    private double? _departureOrbitPeriapsisKm;
    private double? _departureOrbitApoapsisKm;

    public RocketBuilderForm(UiLanguage language, RocketBuilderMissionRequest? seed = null)
    {
        _language = language;
        _predictorService = new RocketBuilderPredictorService();

        InitializeWindow();
        BuildUi();
        PopulateChoices();
        ApplyDefaults();
        if (seed is not null)
        {
            LoadRequestIntoForm(seed);
            SetStatus(L("Planner values were prefilled.", "Значения из планировщика уже подставлены."));
        }
        else
        {
            RefreshRequestPreview();
            SetStatus(L("Fill in the mission and run the model.", "Заполни миссию и запусти модель."));
        }
    }

    private void InitializeWindow()
    {
        SuspendLayout();
        Text = L("Rocket Builder AI", "AI-конструктор ракеты");
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1460, 940);
        BackColor = UiTheme.WindowBackground;
        ResumeLayout(false);
    }

    private void BuildUi()
    {
        SuspendLayout();
        Controls.Clear();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = UiTheme.WindowBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 10),
            BackColor = UiTheme.ToolbarBackground,
            WrapContents = false,
            AutoScroll = true
        };
        actions.Controls.Add(ActionButton(L("Predict", "Подобрать"), async (_, _) => await PredictAsync(), UiTheme.AccentGreen));
        actions.Controls.Add(ActionButton(L("Load Request JSON", "Загрузить JSON запроса"), (_, _) => LoadRequestFromJson()));
        actions.Controls.Add(ActionButton(L("Save Request JSON", "Сохранить JSON запроса"), (_, _) => SaveRequestToJson()));
        actions.Controls.Add(new Label { AutoSize = true, Text = L("Default model", "Модель по умолчанию"), Margin = new Padding(20, 10, 8, 0) });
        actions.Controls.Add(new Label { AutoSize = true, Text = Path.GetFileName(_predictorService.DefaultModelDirectory), Margin = new Padding(0, 10, 8, 0), ForeColor = UiTheme.TextSecondary });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 520,
            BackColor = UiTheme.Border,
            Panel1MinSize = 420
        };
        split.Panel1.Padding = new Padding(16);
        split.Panel2.Padding = new Padding(16);
        split.Panel1.Controls.Add(BuildLeftPanel());
        split.Panel2.Controls.Add(BuildRightPanel());

        var statusPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 6, 12, 2),
            BackColor = UiTheme.ToolbarBackground
        };
        statusPanel.Controls.Add(_status);

        root.Controls.Add(actions, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(statusPanel, 0, 2);
        Controls.Add(root);
        UiTheme.Apply(this);

        ResumeLayout(true);
    }

    private Control BuildLeftPanel()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.PanelBackground };
        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSection(content, BuildInfoCard());
        AddSection(content, Group(L("Runtime", "Среда"),
            (L("Model", "Модель"), _modelSelector),
            (L("Device", "Устройство"), _device),
            (L("Top candidates", "Топ вариантов"), _topK)));
        AddSection(content, Group(L("Mission basics", "Основные параметры миссии"),
            (L("Origin planet", "Планета отправления"), _origin),
            (L("Destination planet", "Планета назначения"), _destination),
            (L("Payload mass, kg", "Полезная масса, кг"), _payloadMassKg),
            (L("Travel days", "Длительность перелета, дни"), _travelDays),
            (L("Mean solar distance, AU", "Средняя дистанция от Солнца, а.е."), _meanDistanceAu),
            (L("Min solar distance, AU", "Минимальная дистанция от Солнца, а.е."), _minDistanceAu)));
        AddSection(content, Group(L("Mission delta-v budget", "Бюджет delta-v миссии"),
            (L("Mission total (excl. reserve), m/s", "Общее delta-v без резерва, м/с"), _dvTotalMps),
            (L("Surface -> Orbit, m/s", "Поверхность -> орбита, м/с"), _surfaceToOrbitDvMps),
            (L("Orbit -> TMI, m/s", "Орбита -> TMI, м/с"), _orbitToTmiDvMps),
            (L("MOI / arrival burn, m/s", "MOI / торможение на прибытии, м/с"), _moiDvMps),
            (L("Correction reserve delta-v, m/s", "Резерв коррекций delta-v, м/с"), _correctionReserveDv),
            (L("Departure stack subtotal, m/s", "Сумма старта и TMI, м/с"), _departureStackSummary),
            (L("Breakdown total, m/s", "Сумма по разбивке, м/с"), _breakdownTotalSummary),
            (L("Total incl. reserve, m/s", "Итого с резервом, м/с"), _allocationTotalSummary)));
        AddSection(content, Group(L("Trajectory context", "Контекст траектории"),
            (L("Departure orbit, km", "Стартовая орбита, км"), _departureOrbitKm),
            (L("Arrival orbit, km", "Орбита прибытия, км"), _arrivalOrbitKm),
            (L("Departure distance, AU", "Дистанция на старте, а.е."), _departureDistanceAu),
            (L("Arrival distance, AU", "Дистанция на прибытии, а.е."), _arrivalDistanceAu),
            (L("Departure solar flux, W/m^2", "Солнечный поток на старте, Вт/м^2"), _departureFlux),
            (L("Mean solar flux, W/m^2", "Средний солнечный поток, Вт/м^2"), _meanFlux),
            (L("Phase angle, deg", "Фазовый угол, град"), _phaseAngleDeg),
            (L("Transfer angle, deg", "Угол перелета, град"), _transferAngleDeg),
            (L("Long-way mode", "Режим long-way"), _longWay)));
        AddSection(content, Group(L("Prepared request", "Собранный запрос"),
            (L("Request JSON", "JSON запроса"), WrapFill(_requestJson, 300))));

        scroll.Controls.Add(content);
        return scroll;
    }

    private Control BuildRightPanel()
    {
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildSummaryTab());
        tabs.TabPages.Add(BuildAlternativesTab());
        tabs.TabPages.Add(BuildJsonTab(L("Response JSON", "JSON ответа"), _responseJson));
        tabs.TabPages.Add(BuildJsonTab(L("Console", "Консоль"), _consoleLog));
        return tabs;
    }

    private TabPage BuildSummaryTab()
    {
        var page = new TabPage(L("Best design", "Лучший вариант"));
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

        top.Controls.Add(Group(L("Best architecture", "Лучшая архитектура"),
            (L("Architecture key", "Ключ архитектуры"), _bestArchitecture),
            (L("Mission stages", "Ступени миссии"), _bestStageCount),
            (L("Effective stages", "Полноценные ступени"), _bestEffectiveStageCount),
            (L("Interpretation", "Интерпретация"), _bestStageInterpretation),
            (L("Predicted score, kg-eq", "Предсказанный score, кг-экв"), _bestScore),
            (L("Predicted launch mass, kg", "Предсказанная стартовая масса, кг"), _bestLaunchMass),
            (L("Predicted boiloff, kg", "Предсказанный boiloff, кг"), _bestBoiloff),
            (L("Inference device", "Устройство инференса"), _bestDevice)), 0, 0);
        top.Controls.Add(Group(L("Why this design", "Почему выбран этот вариант"),
            (L("Reasoning", "Объяснение"), WrapFill(_reasoning, 180))), 1, 0);

        var stagesGroup = new ThemedGroupBox { Text = L("Stage breakdown", "Ступени"), Dock = DockStyle.Fill, Padding = new Padding(12, 30, 12, 12) };
        stagesGroup.Controls.Add(_stagesGrid);

        root.Controls.Add(top, 0, 0);
        root.Controls.Add(stagesGroup, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildAlternativesTab()
    {
        var page = new TabPage(L("Alternatives", "Альтернативы"));
        var group = new ThemedGroupBox { Text = L("Top candidates", "Топ-кандидаты"), Dock = DockStyle.Fill, Padding = new Padding(12, 30, 12, 12) };
        group.Controls.Add(_candidatesGrid);
        page.Controls.Add(group);
        return page;
    }

    private TabPage BuildJsonTab(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private Control BuildInfoCard()
    {
        var panel = new Panel { Tag = "info-card", Padding = new Padding(16), BackColor = UiTheme.CardBackground, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 14) };
        var title = new Label
        {
            AutoSize = true,
            Text = L("Neural rocket architect on top of your trained model", "Нейросетевой конструктор ракеты поверх обученной модели"),
            Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Dock = DockStyle.Top
        };
        var body = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Text = L(
                "This screen keeps Surface -> Orbit, Orbit -> TMI, MOI, and correction reserve separate while still building a compatible rocket_builder_ml request.",
                "Этот экран отдельно показывает Поверхность -> орбита, Орбита -> TMI, MOI и резерв коррекций, но при этом собирает совместимый запрос для rocket_builder_ml."),
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = UiTheme.TextSecondary,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0)
        };
        panel.Controls.Add(body);
        panel.Controls.Add(title);
        return panel;
    }

    private void PopulateChoices()
    {
        PopulateModelChoices();
        SetChoiceItems(_device, "auto",
            new ChoiceItem("auto", L("auto", "авто")),
            new ChoiceItem("cuda", "cuda"),
            new ChoiceItem("cpu", "cpu"));
        SetChoiceItems(_longWay, "auto",
            new ChoiceItem("auto", L("auto", "авто")),
            new ChoiceItem("short", L("short-way", "короткий путь")),
            new ChoiceItem("long", L("long-way", "длинный путь")));
        PopulatePlanetChoices(_origin);
        PopulatePlanetChoices(_destination);
    }

    private void ApplyDefaults()
    {
        _origin.SelectedIndex = 0;
        _destination.SelectedIndex = 0;
        _surfaceLaunch = null;
        _launchAscentDvMps = null;
        _departureOrbitPeriapsisKm = null;
        _departureOrbitApoapsisKm = null;
        _bestArchitecture.Clear();
        _bestStageCount.Clear();
        _bestEffectiveStageCount.Clear();
        _bestStageInterpretation.Clear();
        _bestScore.Clear();
        _bestLaunchMass.Clear();
        _bestBoiloff.Clear();
        _bestDevice.Clear();
        _reasoning.Clear();
        _responseJson.Clear();
        _consoleLog.Clear();
        _departureStackSummary.Clear();
        _breakdownTotalSummary.Clear();
        _allocationTotalSummary.Clear();
        WirePreviewEvents();
    }

    private void WirePreviewEvents()
    {
        foreach (var control in new Control[]
                 {
                     _modelSelector, _origin, _destination, _payloadMassKg, _dvTotalMps, _travelDays,
                     _meanDistanceAu, _minDistanceAu, _surfaceToOrbitDvMps, _orbitToTmiDvMps, _moiDvMps, _departureOrbitKm, _arrivalOrbitKm,
                     _departureDistanceAu, _arrivalDistanceAu, _departureFlux, _meanFlux, _phaseAngleDeg, _transferAngleDeg,
                     _correctionReserveDv, _longWay, _device, _topK
                 })
        {
            switch (control)
            {
                case ComboBox combo:
                    combo.SelectedIndexChanged += (_, _) => RefreshRequestPreview();
                    break;
                case NumericUpDown number:
                    number.ValueChanged += (_, _) => RefreshRequestPreview();
                    break;
                case TextBox text:
                    text.TextChanged += (_, _) => RefreshRequestPreview();
                    break;
            }
        }
    }

    private async Task PredictAsync()
    {
        RocketBuilderMissionRequest request;
        try
        {
            request = BuildRequestFromForm();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }

        SetBusy(true);
        SetStatus(L("Running prediction...", "Запускаю предсказание..."));

        try
        {
            var execution = await _predictorService.PredictAsync(
                request,
                SelectedModelDirectory(),
                "python",
                SelectedKey(_device, "auto"),
                (int)_topK.Value);

            ApplyPrediction(execution);
            SetStatus(L("Prediction completed.", "Предсказание завершено."));
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyPrediction(RocketBuilderPredictionExecution execution)
    {
        var best = execution.Payload.Best;
        _bestArchitecture.Text = best.ArchitectureKey;
        _bestStageCount.Text = best.StageCount.ToString(CultureInfo.InvariantCulture);
        _bestEffectiveStageCount.Text = best.EffectiveStageCount.ToString(CultureInfo.InvariantCulture);
        _bestStageInterpretation.Text = FormatStageInterpretation(best);
        _bestScore.Text = FormatMass(best.PredictedScoreKgEquivalent);
        _bestLaunchMass.Text = FormatMass(best.PredictedLaunchMassKg);
        _bestBoiloff.Text = FormatMass(best.PredictedBoiloffMassKg);
        _bestDevice.Text = execution.Payload.Device;
        _reasoning.Text = execution.Payload.Reasoning.Count == 0
            ? L("No reasoning returned.", "Модель не вернула пояснение.")
            : string.Join(Environment.NewLine, execution.Payload.Reasoning.Select(line => "- " + line));

        _requestJson.Text = execution.RequestJson;
        _responseJson.Text = execution.ResponseJson;
        _consoleLog.Text = BuildConsoleText(execution.StandardOutput, execution.StandardError, execution.ModelDirectory, execution.WorkingDirectory);

        _stagesGrid.DataSource = best.Stages
            .Select(stage => new
            {
                Stage = stage.StageNumber,
                Kind = LocalizeStageKind(stage.StageKind),
                Role = stage.Role,
                Segment = stage.Segment,
                Fuel = stage.PropellantLabel,
                FuelKey = stage.PropellantKey,
                Engine = stage.EngineLabel,
                EngineKey = stage.EngineKey,
                Engines = stage.EngineCount,
                ThrustKn = stage.AvailableThrustKn,
                IspS = stage.EngineVacuumIspSeconds,
                TankKg = stage.TankMassKg,
                DvMps = stage.DvMps,
                DvSharePercent = stage.DvShare * 100.0
            })
            .ToList();

        _candidatesGrid.DataSource = execution.Payload.TopCandidates
            .Select((candidate, index) => new
            {
                Rank = index + 1,
                Architecture = candidate.ArchitectureKey,
                Stages = FormatStageInterpretation(candidate),
                EffectiveStages = candidate.EffectiveStageCount,
                Score = candidate.PredictedScoreKgEquivalent,
                EngineeringScore = candidate.EstimatedTotalScoreKgEquivalent,
                LaunchMass = candidate.PredictedLaunchMassKg,
                Boiloff = candidate.PredictedBoiloffMassKg
            })
            .ToList();
    }

    private static string BuildConsoleText(string stdOut, string stdErr, string modelDirectory, string workingDirectory)
    {
        var builder = new List<string>
        {
            $"model:  {modelDirectory}",
            $"cwd:    {workingDirectory}"
        };

        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            builder.Add(string.Empty);
            builder.Add("stdout:");
            builder.Add(stdOut.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            builder.Add(string.Empty);
            builder.Add("stderr:");
            builder.Add(stdErr.Trim());
        }

        return string.Join(Environment.NewLine, builder);
    }

    private void LoadRequestFromJson()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = L("JSON files (*.json)|*.json|All files (*.*)|*.*", "Файлы JSON (*.json)|*.json|Все файлы (*.*)|*.*"),
            Title = L("Open request JSON", "Открыть JSON запроса")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<RocketBuilderMissionRequest>(File.ReadAllText(dialog.FileName), RequestJsonOptions)
                          ?? throw new InvalidOperationException(L("Request JSON could not be parsed.", "Не удалось разобрать JSON запроса."));
            LoadRequestIntoForm(payload);
            SetStatus(L($"Loaded request from {dialog.FileName}", $"Запрос загружен из {dialog.FileName}"));
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private void SaveRequestToJson()
    {
        RocketBuilderMissionRequest request;
        try
        {
            request = BuildRequestFromForm();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = L("JSON files (*.json)|*.json|All files (*.*)|*.*", "Файлы JSON (*.json)|*.json|Все файлы (*.*)|*.*"),
            Title = L("Save request JSON", "Сохранить JSON запроса"),
            FileName = "rocket-builder-request.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(request, RequestJsonOptions);
            File.WriteAllText(dialog.FileName, json + Environment.NewLine);
            SetStatus(L($"Request saved to {dialog.FileName}", $"Запрос сохранен в {dialog.FileName}"));
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private void LoadRequestIntoForm(RocketBuilderMissionRequest request)
    {
        _suspendPreview = true;
        try
        {
            SelectPlanet(_origin, request.Origin);
            SelectPlanet(_destination, request.Destination);
            _payloadMassKg.Value = Clamp(request.PayloadMassKg, _payloadMassKg);
            _dvTotalMps.Value = Clamp(request.DvTotalMps, _dvTotalMps);
            _travelDays.Value = Clamp(request.TravelDays, _travelDays);
            _meanDistanceAu.Value = Clamp(request.MeanDistanceAu, _meanDistanceAu);
            _minDistanceAu.Value = Clamp(request.MinDistanceAu, _minDistanceAu);
            _surfaceLaunch = request.SurfaceLaunch;
            _launchAscentDvMps = request.LaunchAscentDvMps;
            _departureOrbitPeriapsisKm = request.DepartureOrbitPeriapsisKm;
            _departureOrbitApoapsisKm = request.DepartureOrbitApoapsisKm;

            SetOptionalNumber(_surfaceToOrbitDvMps, request.LaunchAscentDvMps);
            SetOptionalNumber(_orbitToTmiDvMps, ResolveOrbitToTmiDeltaV(request));
            SetOptionalNumber(_moiDvMps, request.DvInjectionMps);
            SetOptionalNumber(_departureOrbitKm, request.DepartureOrbitKm);
            SetOptionalNumber(_arrivalOrbitKm, request.ArrivalOrbitKm);
            SetOptionalNumber(_departureDistanceAu, request.DepartureDistanceAu);
            SetOptionalNumber(_arrivalDistanceAu, request.ArrivalDistanceAu);
            SetOptionalNumber(_departureFlux, request.DepartureSolarFluxWm2);
            SetOptionalNumber(_meanFlux, request.MeanSolarFluxWm2);
            SetOptionalNumber(_phaseAngleDeg, request.PhaseAngleDeg);
            SetOptionalNumber(_transferAngleDeg, request.TransferAngleDeg);
            SetOptionalNumber(_correctionReserveDv, request.CorrectionReserveDvMps);
            SelectChoice(_longWay, request.LongWay switch
            {
                true => "long",
                false => "short",
                _ => "auto"
            });
        }
        finally
        {
            _suspendPreview = false;
        }

        RefreshRequestPreview();
    }

    private RocketBuilderMissionRequest BuildRequestFromForm()
    {
        var origin = PlanetNameOrNull(_origin);
        var destination = PlanetNameOrNull(_destination);
        var surfaceToOrbit = ParseOptionalDouble(_surfaceToOrbitDvMps, L("Surface -> Orbit delta-v", "delta-v Поверхность -> орбита"));
        var orbitToTmi = ParseOptionalDouble(_orbitToTmiDvMps, L("Orbit -> TMI delta-v", "delta-v Орбита -> TMI"));
        var moi = ParseOptionalDouble(_moiDvMps, L("MOI / arrival burn", "MOI / торможение на прибытии"));
        double? departureStack = surfaceToOrbit.HasValue || orbitToTmi.HasValue
            ? Math.Max(0.0, surfaceToOrbit.GetValueOrDefault()) + Math.Max(0.0, orbitToTmi.GetValueOrDefault())
            : null;
        if (!string.IsNullOrWhiteSpace(origin) &&
            !string.IsNullOrWhiteSpace(destination) &&
            origin.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(L("Origin and destination must be different planets.", "Планета отправления и назначения должны отличаться."));
        }

        return new RocketBuilderMissionRequest
        {
            Origin = origin,
            Destination = destination,
            PayloadMassKg = (double)_payloadMassKg.Value,
            DvTotalMps = (double)_dvTotalMps.Value,
            TravelDays = (double)_travelDays.Value,
            MeanDistanceAu = (double)_meanDistanceAu.Value,
            MinDistanceAu = (double)_minDistanceAu.Value,
            LaunchAscentDvMps = surfaceToOrbit,
            DvEjectionMps = departureStack,
            DvInjectionMps = moi,
            DepartureOrbitKm = ParseOptionalDouble(_departureOrbitKm, L("Departure orbit", "Стартовая орбита")),
            DepartureOrbitPeriapsisKm = _departureOrbitPeriapsisKm,
            DepartureOrbitApoapsisKm = _departureOrbitApoapsisKm,
            ArrivalOrbitKm = ParseOptionalDouble(_arrivalOrbitKm, L("Arrival orbit", "Орбита прибытия")),
            DepartureDistanceAu = ParseOptionalDouble(_departureDistanceAu, L("Departure distance", "Дистанция на старте")),
            ArrivalDistanceAu = ParseOptionalDouble(_arrivalDistanceAu, L("Arrival distance", "Дистанция на прибытии")),
            DepartureSolarFluxWm2 = ParseOptionalDouble(_departureFlux, L("Departure solar flux", "Солнечный поток на старте")),
            MeanSolarFluxWm2 = ParseOptionalDouble(_meanFlux, L("Mean solar flux", "Средний солнечный поток")),
            PhaseAngleDeg = ParseOptionalDouble(_phaseAngleDeg, L("Phase angle", "Фазовый угол")),
            TransferAngleDeg = ParseOptionalDouble(_transferAngleDeg, L("Transfer angle", "Угол перелета")),
            CorrectionReserveDvMps = ParseOptionalDouble(_correctionReserveDv, L("Correction reserve delta-v", "Резерв коррекций delta-v")),
            SurfaceLaunch = surfaceToOrbit.HasValue && surfaceToOrbit.Value > 0.0 ? true : _surfaceLaunch,
            DepartureSurfaceGravityMps2 = RocketBuilderMissionDefaults.ResolveDepartureSurfaceGravityMps2(origin),
            LongWay = SelectedKey(_longWay, "auto") switch
            {
                "short" => false,
                "long" => true,
                _ => null
            }
        };
    }

    private void RefreshRequestPreview()
    {
        if (_suspendPreview)
        {
            return;
        }

        RefreshDeltaVBudgetPreview();

        try
        {
            _requestJson.Text = JsonSerializer.Serialize(BuildRequestFromForm(), RequestJsonOptions);
        }
        catch (Exception ex)
        {
            _requestJson.Text = ex.Message;
        }
    }

    private void PopulateModelChoices()
    {
        var models = _predictorService.GetAvailableModels();
        _modelSelector.BeginUpdate();
        _modelSelector.Items.Clear();
        foreach (var model in models)
        {
            _modelSelector.Items.Add(model);
        }
        _modelSelector.EndUpdate();

        if (_modelSelector.Items.Count == 0)
        {
            return;
        }

        var defaultModelDirectory = _predictorService.DefaultModelDirectory;
        for (var i = 0; i < _modelSelector.Items.Count; i++)
        {
            if (_modelSelector.Items[i] is RocketBuilderModelOption option &&
                string.Equals(option.DirectoryPath, defaultModelDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _modelSelector.SelectedIndex = i;
                return;
            }
        }

        _modelSelector.SelectedIndex = 0;
    }

    private string SelectedModelDirectory()
    {
        if (_modelSelector.SelectedItem is RocketBuilderModelOption option)
        {
            return option.DirectoryPath;
        }

        throw new InvalidOperationException(L("Choose a trained model first.", "Сначала выбери обученную модель."));
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        Enabled = !busy;
        UseWaitCursor = busy;
        Refresh();
    }

    private void SetStatus(string text) => _status.Text = text;

    private void Error(string message)
    {
        SetStatus(message);
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static void AddSection(TableLayoutPanel layout, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 0, 0, 14);
        control.Dock = DockStyle.Top;
        layout.Controls.Add(control, 0, row);
    }

    private GroupBox Group(string title, params (string Label, Control Control)[] rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < rows.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = rows[i].Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 7, 8, 8),
                MinimumSize = new Size(0, 24)
            }, 0, i);

            rows[i].Control.Dock = DockStyle.Fill;
            rows[i].Control.Margin = new Padding(0, 2, 0, 8);
            table.Controls.Add(rows[i].Control, 1, i);
        }

        var group = new ThemedGroupBox
        {
            Text = title,
            Padding = new Padding(14, 30, 14, 14),
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 14)
        };
        group.Controls.Add(table);
        return group;
    }

    private static Control WrapFill(Control control, int height)
    {
        control.Dock = DockStyle.Fill;
        var panel = new Panel { Dock = DockStyle.Fill, Height = height };
        panel.Controls.Add(control);
        return panel;
    }

    private static Button ActionButton(string text, EventHandler onClick, Color? color = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(132, 36),
            Height = 36,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 8, 0)
        };
        UiTheme.StyleActionButton(button, color);
        button.Click += onClick;
        return button;
    }

    private static DataGridView BuildStagesGrid()
    {
        var grid = BuildGrid();
        grid.Columns.Add(TextColumn("Stage", "Stage", 60));
        grid.Columns.Add(TextColumn("Kind", "Kind", 90));
        grid.Columns.Add(TextColumn("Role", "Role", 120));
        grid.Columns.Add(TextColumn("Segment", "Segment", 110));
        grid.Columns.Add(TextColumn("Fuel", "Fuel", 140));
        grid.Columns.Add(TextColumn("FuelKey", "Fuel key", 120));
        grid.Columns.Add(TextColumn("Engine", "Engine", 150));
        grid.Columns.Add(TextColumn("EngineKey", "Engine key", 130));
        grid.Columns.Add(TextColumn("Engines", "Engines", 70));
        grid.Columns.Add(TextColumn("ThrustKn", "Thrust, kN", 95, "N0"));
        grid.Columns.Add(TextColumn("IspS", "Isp, s", 80, "N1"));
        grid.Columns.Add(TextColumn("TankKg", "Tank, kg", 90, "N0"));
        grid.Columns.Add(TextColumn("DvMps", "dV, m/s", 95, "N0"));
        grid.Columns.Add(TextColumn("DvSharePercent", "dV, %", 80, "N1"));
        return grid;
    }

    private static DataGridView BuildCandidatesGrid()
    {
        var grid = BuildGrid();
        grid.Columns.Add(TextColumn("Rank", "Rank", 60));
        grid.Columns.Add(TextColumn("Architecture", "Architecture", 260));
        grid.Columns.Add(TextColumn("Stages", "Stages", 150));
        grid.Columns.Add(TextColumn("EffectiveStages", "Effective", 80));
        grid.Columns.Add(TextColumn("Score", "Score", 120, "N0"));
        grid.Columns.Add(TextColumn("EngineeringScore", "Engineering", 120, "N0"));
        grid.Columns.Add(TextColumn("LaunchMass", "Launch Mass", 120, "N0"));
        grid.Columns.Add(TextColumn("Boiloff", "Boiloff", 110, "N0"));
        return grid;
    }

    private static DataGridView BuildGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = UiTheme.InputBackground,
            BorderStyle = BorderStyle.FixedSingle
        };

        UiTheme.StyleDataGridView(grid);
        return grid;
    }

    private static DataGridViewTextBoxColumn TextColumn(string dataPropertyName, string headerText, int width, string? format = null)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = dataPropertyName,
            HeaderText = headerText,
            Width = width,
            DefaultCellStyle = format is null ? new DataGridViewCellStyle() : new DataGridViewCellStyle { Format = format }
        };
    }

    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10F), BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private static NumericUpDown Num(decimal value, decimal min, decimal max, int decimals) => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, ThousandsSeparator = true, Font = new Font("Segoe UI", 10F), BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
    private static TextBox LineBox() => new() { Font = new Font("Segoe UI", 10F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox OptionalNumberBox() => new() { Font = new Font("Segoe UI", 10F), BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "optional", BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox ReadOnlyLine() => new() { ReadOnly = true, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.CardAltBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox ReadOnlyArea() => new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.25F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.CardAltBackground, ForeColor = UiTheme.TextPrimary };
    private static TextBox CodeBox() => new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F), BorderStyle = BorderStyle.FixedSingle, BackColor = UiTheme.InputBackground, ForeColor = UiTheme.TextPrimary };

    private void RefreshDeltaVBudgetPreview()
    {
        var surfaceToOrbit = TryParseOptionalDouble(_surfaceToOrbitDvMps);
        var orbitToTmi = TryParseOptionalDouble(_orbitToTmiDvMps);
        var moi = TryParseOptionalDouble(_moiDvMps);
        var correctionReserve = TryParseOptionalDouble(_correctionReserveDv);

        double? departureStack = surfaceToOrbit.HasValue || orbitToTmi.HasValue
            ? Math.Max(0.0, surfaceToOrbit.GetValueOrDefault()) + Math.Max(0.0, orbitToTmi.GetValueOrDefault())
            : null;
        double? breakdownTotal = departureStack.HasValue || moi.HasValue
            ? Math.Max(0.0, departureStack.GetValueOrDefault()) + Math.Max(0.0, moi.GetValueOrDefault())
            : null;
        var allocationTotal = (breakdownTotal ?? (double)_dvTotalMps.Value) + Math.Max(0.0, correctionReserve.GetValueOrDefault());

        SetOptionalNumber(_departureStackSummary, departureStack);
        SetOptionalNumber(_breakdownTotalSummary, breakdownTotal);
        SetOptionalNumber(_allocationTotalSummary, allocationTotal);
    }

    private static double? TryParseOptionalDouble(TextBox textBox)
    {
        var text = textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static double? ResolveOrbitToTmiDeltaV(RocketBuilderMissionRequest request)
    {
        if (!request.DvEjectionMps.HasValue)
        {
            return null;
        }

        var ascent = Math.Max(0.0, request.LaunchAscentDvMps.GetValueOrDefault());
        return Math.Max(0.0, request.DvEjectionMps.Value - ascent);
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

    private static string SelectedKey(ComboBox combo, string fallback) => combo.SelectedItem is ChoiceItem item ? item.Key : fallback;

    private void PopulatePlanetChoices(ComboBox combo)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.Add(new ChoiceItem(string.Empty, L("(optional)", "(необязательно)")));
        foreach (var planet in Core.SolarSystemCatalog.PlanetNames)
        {
            combo.Items.Add(new ChoiceItem(planet, planet));
        }
        combo.EndUpdate();
    }

    private static void SelectPlanet(ComboBox combo, string? value)
    {
        foreach (var item in combo.Items.OfType<ChoiceItem>())
        {
            if (string.Equals(item.Key, value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static string? PlanetNameOrNull(ComboBox combo)
    {
        return combo.SelectedItem is ChoiceItem item && !string.IsNullOrWhiteSpace(item.Key)
            ? item.Key
            : null;
    }

    private static decimal Clamp(double value, NumericUpDown control)
    {
        return Math.Min(control.Maximum, Math.Max(control.Minimum, (decimal)value));
    }

    private static void SetOptionalNumber(TextBox textBox, double? value)
    {
        textBox.Text = value.HasValue
            ? value.Value.ToString("0.########", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static double? ParseOptionalDouble(TextBox textBox, string fieldName)
    {
        var text = textBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{fieldName}: expected a number in invariant format, for example 1234.56");
        }

        return value;
    }

    private static string FormatMass(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private string LocalizeStageKind(string stageKind)
    {
        return stageKind switch
        {
            "launch" => L("launch", "стартовая"),
            "service" => L("service", "сервисная"),
            "arrival" => L("arrival", "прибытие"),
            _ => stageKind
        };
    }

    private string FormatStageInterpretation(RocketBuilderCandidatePrediction candidate)
    {
        if (candidate.ServiceStageCount > 0)
        {
            return L(
                $"{candidate.LaunchStageCount} launch + {candidate.ServiceStageCount} service",
                $"{candidate.LaunchStageCount} старт. + {candidate.ServiceStageCount} сервис."
            );
        }

        return candidate.EffectiveStageCount == candidate.StageCount
            ? candidate.StageCount.ToString(CultureInfo.InvariantCulture)
            : $"{candidate.EffectiveStageCount}/{candidate.StageCount}";
    }

    private string L(string english, string russian) => _language == UiLanguage.Russian ? russian : english;
}
