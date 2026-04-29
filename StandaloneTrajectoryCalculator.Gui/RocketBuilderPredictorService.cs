using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core = StandaloneTrajectoryCalculator;

namespace StandaloneTrajectoryCalculator.Gui;

internal sealed class RocketBuilderMissionRequest
{
    [JsonPropertyName("dv_total_mps")]
    public double DvTotalMps { get; set; }

    [JsonPropertyName("travel_days")]
    public double TravelDays { get; set; }

    [JsonPropertyName("mean_distance_au")]
    public double MeanDistanceAu { get; set; }

    [JsonPropertyName("min_distance_au")]
    public double MinDistanceAu { get; set; }

    [JsonPropertyName("payload_mass_kg")]
    public double PayloadMassKg { get; set; }

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; set; }

    [JsonPropertyName("destination")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Destination { get; set; }

    [JsonPropertyName("dv_ejection_mps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DvEjectionMps { get; set; }

    [JsonPropertyName("dv_injection_mps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DvInjectionMps { get; set; }

    [JsonPropertyName("launch_ascent_dv_mps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LaunchAscentDvMps { get; set; }

    [JsonPropertyName("departure_orbit_km")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DepartureOrbitKm { get; set; }

    [JsonPropertyName("departure_orbit_periapsis_km")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DepartureOrbitPeriapsisKm { get; set; }

    [JsonPropertyName("departure_orbit_apoapsis_km")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DepartureOrbitApoapsisKm { get; set; }

    [JsonPropertyName("arrival_orbit_km")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ArrivalOrbitKm { get; set; }

    [JsonPropertyName("departure_distance_au")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DepartureDistanceAu { get; set; }

    [JsonPropertyName("arrival_distance_au")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ArrivalDistanceAu { get; set; }

    [JsonPropertyName("departure_solar_flux_w_m2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DepartureSolarFluxWm2 { get; set; }

    [JsonPropertyName("mean_solar_flux_w_m2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MeanSolarFluxWm2 { get; set; }

    [JsonPropertyName("phase_angle_deg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PhaseAngleDeg { get; set; }

    [JsonPropertyName("transfer_angle_deg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TransferAngleDeg { get; set; }

    [JsonPropertyName("long_way")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LongWay { get; set; }

    [JsonPropertyName("surface_launch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SurfaceLaunch { get; set; }

    [JsonPropertyName("departure_surface_gravity_mps2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DepartureSurfaceGravityMps2 { get; set; }

    [JsonPropertyName("correction_reserve_dv_mps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CorrectionReserveDvMps { get; set; }
}

internal static class RocketBuilderMissionDefaults
{
    public static double? ResolveDepartureSurfaceGravityMps2(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        try
        {
            var body = Core.SolarSystemCatalog.CreateBodyInput(origin);
            if (body.GravitationalParameter <= 0.0 || body.Radius <= 0.0)
            {
                return null;
            }

            return body.GravitationalParameter / (body.Radius * body.Radius);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class RocketBuilderStagePrediction
{
    [JsonPropertyName("stage_number")]
    public int StageNumber { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("segment")]
    public string Segment { get; set; } = string.Empty;

    [JsonPropertyName("propellant_key")]
    public string PropellantKey { get; set; } = string.Empty;

    [JsonPropertyName("propellant_label")]
    public string PropellantLabel { get; set; } = string.Empty;

    [JsonPropertyName("engine_key")]
    public string EngineKey { get; set; } = string.Empty;

    [JsonPropertyName("engine_label")]
    public string EngineLabel { get; set; } = string.Empty;

    [JsonPropertyName("engine_count")]
    public int EngineCount { get; set; }

    [JsonPropertyName("engine_vacuum_isp_seconds")]
    public double EngineVacuumIspSeconds { get; set; }

    [JsonPropertyName("engine_vacuum_thrust_kn")]
    public double EngineVacuumThrustKn { get; set; }

    [JsonPropertyName("available_thrust_kn")]
    public double AvailableThrustKn { get; set; }

    [JsonPropertyName("tank_mass_kg")]
    public double TankMassKg { get; set; }

    [JsonPropertyName("dv_share")]
    public double DvShare { get; set; }

    [JsonPropertyName("dv_mps")]
    public double DvMps { get; set; }

    [JsonPropertyName("stage_kind")]
    public string StageKind { get; set; } = string.Empty;

    [JsonPropertyName("is_service_stage")]
    public bool IsServiceStage { get; set; }
}

internal sealed class RocketBuilderCandidatePrediction
{
    [JsonPropertyName("architecture_key")]
    public string ArchitectureKey { get; set; } = string.Empty;

    [JsonPropertyName("stage_count")]
    public int StageCount { get; set; }

    [JsonPropertyName("launch_stage_count")]
    public int LaunchStageCount { get; set; }

    [JsonPropertyName("effective_stage_count")]
    public int EffectiveStageCount { get; set; }

    [JsonPropertyName("service_stage_count")]
    public int ServiceStageCount { get; set; }

    [JsonPropertyName("stage_count_summary")]
    public string StageCountSummary { get; set; } = string.Empty;

    [JsonPropertyName("predicted_score_kg_equivalent")]
    public double PredictedScoreKgEquivalent { get; set; }

    [JsonPropertyName("estimated_total_score_kg_equivalent")]
    public double EstimatedTotalScoreKgEquivalent { get; set; }

    [JsonPropertyName("predicted_launch_mass_kg")]
    public double PredictedLaunchMassKg { get; set; }

    [JsonPropertyName("predicted_boiloff_mass_kg")]
    public double PredictedBoiloffMassKg { get; set; }

    [JsonPropertyName("allocation_total_dv_mps")]
    public double AllocationTotalDvMps { get; set; }

    [JsonPropertyName("stage_dv_source")]
    public string StageDvSource { get; set; } = string.Empty;

    [JsonPropertyName("stages")]
    public List<RocketBuilderStagePrediction> Stages { get; set; } = [];
}

internal sealed class RocketBuilderPredictionPayload
{
    [JsonPropertyName("request")]
    public RocketBuilderMissionRequest Request { get; set; } = new();

    [JsonPropertyName("best")]
    public RocketBuilderCandidatePrediction Best { get; set; } = new();

    [JsonPropertyName("reasoning")]
    public List<string> Reasoning { get; set; } = [];

    [JsonPropertyName("top_candidates")]
    public List<RocketBuilderCandidatePrediction> TopCandidates { get; set; } = [];

    [JsonPropertyName("device")]
    public string Device { get; set; } = string.Empty;

    [JsonPropertyName("stage_dv_head_trained")]
    public bool StageDvHeadTrained { get; set; }
}

internal sealed class RocketBuilderPredictionExecution
{
    public required RocketBuilderPredictionPayload Payload { get; init; }
    public required string RequestJson { get; init; }
    public required string ResponseJson { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public required int ExitCode { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string PythonExecutable { get; init; }
    public required string ModelDirectory { get; init; }
}

internal sealed class RocketBuilderModelOption
{
    public required string Name { get; init; }
    public required string DirectoryPath { get; init; }

    public override string ToString() => Name;
}

internal static class RocketBuilderWorkspace
{
    public static string FindRepositoryRoot()
    {
        foreach (var candidate in EnumerateCandidateDirectories())
        {
            if (File.Exists(Path.Combine(candidate, "rocket_builder_ml", "predict.py")) &&
                Directory.Exists(Path.Combine(candidate, "StandaloneTrajectoryCalculator.Gui")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find the repository root that contains rocket_builder_ml and StandaloneTrajectoryCalculator.Gui.");
    }

    public static string FindDefaultModelDirectory(string repositoryRoot)
    {
        var availableModels = EnumerateModelDirectories(repositoryRoot).ToList();
        foreach (var name in new[]
                 {
                     "rocket-builder-ml-v7-realism-5070-8gb",
                     "rocket-builder-ml-tank-aware-outer-big",
                     "rocket-builder-ml-tank-aware-big",
                     "rocket-builder-ml-tank-aware",
                     "rocket-builder-ml-engine-aware",
                     "rocket-builder-ml-engine-aware-big",
                     "rocket-builder-ml-v5-cryo-oversize-big",
                     "rocket-builder-ml-v4-arrival-penalty-big",
                     "rocket-builder-ml-v3-stage-dv-big-retrained",
                     "rocket-builder-ml-v3-stage-dv-big",
                     "rocket-builder-ml-v3-stage-dv",
                     "rocket-builder-ml-v2-big",
                     "rocket-builder-ml-v2",
                     "rocket-builder-ml"
                 })
        {
            var preferred = availableModels.FirstOrDefault(model =>
                string.Equals(model.Name, name, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred.DirectoryPath;
            }
        }

        return availableModels
            .OrderByDescending(model => Directory.GetLastWriteTimeUtc(model.DirectoryPath))
            .Select(model => model.DirectoryPath)
            .FirstOrDefault()
            ?? Path.Combine(repositoryRoot, "artifacts", "rocket-builder-ml-engine-aware");
    }

    public static IEnumerable<RocketBuilderModelOption> EnumerateModelDirectories(string repositoryRoot)
    {
        var artifactsDirectory = Path.Combine(repositoryRoot, "artifacts");
        if (!Directory.Exists(artifactsDirectory))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(artifactsDirectory)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(directory, "model.pt")) ||
                !File.Exists(Path.Combine(directory, "metadata.json")))
            {
                continue;
            }

            yield return new RocketBuilderModelOption
            {
                Name = Path.GetFileName(directory),
                DirectoryPath = directory
            };
        }
    }

    public static string ResolvePath(string repositoryRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repositoryRoot, path));
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(seed) || !Directory.Exists(seed))
            {
                continue;
            }

            var current = new DirectoryInfo(seed);
            while (current is not null)
            {
                if (seen.Add(current.FullName))
                {
                    yield return current.FullName;
                }

                current = current.Parent;
            }
        }
    }
}

internal sealed class RocketBuilderPredictorService
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RocketBuilderPredictorService(string? repositoryRoot = null)
    {
        RepositoryRoot = repositoryRoot ?? RocketBuilderWorkspace.FindRepositoryRoot();
    }

    public string RepositoryRoot { get; }

    public string DefaultModelDirectory => RocketBuilderWorkspace.FindDefaultModelDirectory(RepositoryRoot);

    public IReadOnlyList<RocketBuilderModelOption> GetAvailableModels()
    {
        return RocketBuilderWorkspace.EnumerateModelDirectories(RepositoryRoot).ToList();
    }

    public async Task<RocketBuilderPredictionExecution> PredictAsync(
        RocketBuilderMissionRequest request,
        string modelDirectory,
        string pythonExecutable,
        string device,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolvedModelDirectory = RocketBuilderWorkspace.ResolvePath(RepositoryRoot, modelDirectory);
        if (!Directory.Exists(resolvedModelDirectory))
        {
            throw new InvalidOperationException($"Model directory does not exist: {resolvedModelDirectory}");
        }

        var requestPath = Path.Combine(Path.GetTempPath(), $"rocket-builder-request-{Guid.NewGuid():N}.json");
        var responsePath = Path.Combine(Path.GetTempPath(), $"rocket-builder-response-{Guid.NewGuid():N}.json");
        var requestJson = JsonSerializer.Serialize(request, RequestJsonOptions);
        await File.WriteAllTextAsync(requestPath, requestJson + Environment.NewLine, cancellationToken);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(pythonExecutable) ? "python" : pythonExecutable,
                WorkingDirectory = RepositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add("rocket_builder_ml.predict");
            startInfo.ArgumentList.Add("--model-dir");
            startInfo.ArgumentList.Add(resolvedModelDirectory);
            startInfo.ArgumentList.Add("--input-json");
            startInfo.ArgumentList.Add(requestPath);
            startInfo.ArgumentList.Add("--output-json");
            startInfo.ArgumentList.Add(responsePath);
            startInfo.ArgumentList.Add("--device");
            startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(device) ? "auto" : device);
            startInfo.ArgumentList.Add("--top-k");
            startInfo.ArgumentList.Add(Math.Max(1, topK).ToString(CultureInfo.InvariantCulture));

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException($"Could not start Python executable '{startInfo.FileName}'. Check the path in the GUI.", ex);
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryTerminate(process);
                throw;
            }

            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildFailureMessage(process.ExitCode, stdOut, stdErr));
            }

            if (!File.Exists(responsePath))
            {
                throw new InvalidOperationException("Prediction finished without writing the expected JSON output.");
            }

            var responseJson = await File.ReadAllTextAsync(responsePath, cancellationToken);
            var payload = JsonSerializer.Deserialize<RocketBuilderPredictionPayload>(responseJson, ResponseJsonOptions)
                          ?? throw new InvalidOperationException("Prediction JSON could not be parsed.");

            return new RocketBuilderPredictionExecution
            {
                Payload = payload,
                RequestJson = requestJson,
                ResponseJson = responseJson,
                StandardOutput = stdOut,
                StandardError = stdErr,
                ExitCode = process.ExitCode,
                WorkingDirectory = RepositoryRoot,
                PythonExecutable = startInfo.FileName,
                ModelDirectory = resolvedModelDirectory
            };
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    private static string BuildFailureMessage(int exitCode, string stdOut, string stdErr)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Prediction process failed with exit code {exitCode}.");

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            builder.AppendLine();
            builder.AppendLine("stderr:");
            builder.AppendLine(stdErr.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            builder.AppendLine();
            builder.AppendLine("stdout:");
            builder.AppendLine(stdOut.Trim());
        }

        return builder.ToString().TrimEnd();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failures during cancellation.
        }
    }
}
