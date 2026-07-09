using System.Text.Json.Serialization;

namespace StandaloneTrajectoryCalculator.Gui;

public static class RocketBuilderDefaults
{
    public static double? ResolveSurfaceGravity(string? planet) => planet?.ToLowerInvariant() switch
    {
        "mercury" => 3.7, "venus" => 8.87, "earth" => 9.81, "moon" => 1.62,
        "mars" => 3.71, "jupiter" => 24.79, "saturn" => 10.44,
        "uranus" => 8.69, "neptune" => 11.15, _ => null
    };
}

public class RocketBuilderModelOption
{
    public string Name { get; init; } = "";
    public string DirectoryPath { get; init; } = "";
    public override string ToString() => Name;
}

public class RocketBuilderMissionRequest
{
    [JsonPropertyName("origin")] public string? Origin { get; init; }
    [JsonPropertyName("destination")] public string? Destination { get; init; }
    [JsonPropertyName("payload_mass_kg")] public double PayloadMassKg { get; init; }
    [JsonPropertyName("dv_total_mps")] public double DvTotalMps { get; init; }
    [JsonPropertyName("travel_days")] public double TravelDays { get; init; }
    [JsonPropertyName("mean_distance_au")] public double MeanDistanceAu { get; init; }
    [JsonPropertyName("min_distance_au")] public double MinDistanceAu { get; init; }
    [JsonPropertyName("launch_ascent_dv_mps")] public double? LaunchAscentDvMps { get; init; }
    [JsonPropertyName("dv_ejection_mps")] public double? DvEjectionMps { get; init; }
    [JsonPropertyName("dv_injection_mps")] public double? DvInjectionMps { get; init; }
    [JsonPropertyName("correction_reserve_dv_mps")] public double? CorrectionReserveDvMps { get; init; }
    [JsonPropertyName("departure_orbit_km")] public double? DepartureOrbitKm { get; init; }
    [JsonPropertyName("arrival_orbit_km")] public double? ArrivalOrbitKm { get; init; }
    [JsonPropertyName("departure_distance_au")] public double? DepartureDistanceAu { get; init; }
    [JsonPropertyName("arrival_distance_au")] public double? ArrivalDistanceAu { get; init; }
    [JsonPropertyName("departure_solar_flux_wm2")] public double? DepartureSolarFluxWm2 { get; init; }
    [JsonPropertyName("mean_solar_flux_wm2")] public double? MeanSolarFluxWm2 { get; init; }
    [JsonPropertyName("phase_angle_deg")] public double? PhaseAngleDeg { get; init; }
    [JsonPropertyName("transfer_angle_deg")] public double? TransferAngleDeg { get; init; }
    [JsonPropertyName("surface_launch")] public bool? SurfaceLaunch { get; init; }
    [JsonPropertyName("departure_surface_gravity_mps2")] public double? DepartureSurfaceGravityMps2 { get; init; }
    [JsonPropertyName("long_way")] public bool? LongWay { get; init; }
}

public class RocketBuilderPredictionPayload
{
    [JsonPropertyName("best")] public RocketBuilderCandidatePrediction Best { get; init; } = new();
    [JsonPropertyName("top_candidates")] public List<RocketBuilderCandidatePrediction> TopCandidates { get; init; } = [];
    [JsonPropertyName("device")] public string Device { get; init; } = "";
    [JsonPropertyName("reasoning")] public List<string> Reasoning { get; init; } = [];
}

public class RocketBuilderCandidatePrediction
{
    [JsonPropertyName("architecture_key")] public string ArchitectureKey { get; init; } = "";
    [JsonPropertyName("stage_count")] public int StageCount { get; init; }
    [JsonPropertyName("effective_stage_count")] public int EffectiveStageCount { get; init; }
    [JsonPropertyName("launch_stage_count")] public int LaunchStageCount { get; init; }
    [JsonPropertyName("service_stage_count")] public int ServiceStageCount { get; init; }
    [JsonPropertyName("predicted_score_kg_equivalent")] public double PredictedScoreKgEquivalent { get; init; }
    [JsonPropertyName("predicted_launch_mass_kg")] public double PredictedLaunchMassKg { get; init; }
    [JsonPropertyName("predicted_boiloff_mass_kg")] public double PredictedBoiloffMassKg { get; init; }
    [JsonPropertyName("estimated_total_score_kg_equivalent")] public double EstimatedTotalScoreKgEquivalent { get; init; }
    [JsonPropertyName("stages")] public List<RocketBuilderStagePrediction> Stages { get; init; } = [];
}

public class RocketBuilderStagePrediction
{
    [JsonPropertyName("stage_number")] public int StageNumber { get; init; }
    [JsonPropertyName("stage_kind")] public string StageKind { get; init; } = "";
    [JsonPropertyName("role")] public string Role { get; init; } = "";
    [JsonPropertyName("segment")] public string Segment { get; init; } = "";
    [JsonPropertyName("propellant_label")] public string PropellantLabel { get; init; } = "";
    [JsonPropertyName("engine_label")] public string EngineLabel { get; init; } = "";
    [JsonPropertyName("engine_count")] public int EngineCount { get; init; }
    [JsonPropertyName("available_thrust_kn")] public double AvailableThrustKn { get; init; }
    [JsonPropertyName("engine_vacuum_isp_seconds")] public double EngineVacuumIspSeconds { get; init; }
    [JsonPropertyName("tank_mass_kg")] public double TankMassKg { get; init; }
    [JsonPropertyName("dv_mps")] public double DvMps { get; init; }
    [JsonPropertyName("dv_share")] public double DvShare { get; init; }
}

public class RocketBuilderPredictionExecution
{
    public RocketBuilderPredictionPayload Payload { get; init; } = new();
    public string RequestJson { get; init; } = "";
    public string ResponseJson { get; init; } = "";
    public string ModelDirectory { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
}
