from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import json
import math

import numpy as np
import torch

from .architecture_priors import (
    compute_architecture_realism_penalty_kg as compute_architecture_realism_penalty_from_values,
    compute_stage_complexity_prior_kg as compute_stage_complexity_prior_from_values,
    is_surface_launch_context,
)
from .data import ENGINE_PAD_TOKEN, ModelMetadata, NUMERIC_FEATURE_COLUMNS, UNKNOWN_TOKEN, parse_architecture_key
from .engines import EngineSpec, engine_display_map, load_engine_catalog, select_engine_for_stage
from .model import ArchitectureRanker
from .propellants import get_propellant_spec
from .stage_features import DEFAULT_DEPARTURE_STORAGE_DAYS, build_stage_numeric_feature_vector

PROPELLANT_LABELS = {
    "lox_lh2": "LOX/LH2",
    "lox_ch4": "LOX/LCH4",
    "lox_rp1": "LOX/RP-1",
    "nto_mmh": "NTO/MMH",
    "hydrazine": "Hydrazine",
    "none": "None",
}

ENGINE_LABELS = {
    ENGINE_PAD_TOKEN: "None",
    **engine_display_map(),
}

DEPARTURE_SHARE_TEMPLATES = {
    1: np.asarray([1.00], dtype=np.float64),
    2: np.asarray([0.58, 0.42], dtype=np.float64),
    3: np.asarray([0.46, 0.32, 0.22], dtype=np.float64),
    4: np.asarray([0.38, 0.27, 0.20, 0.15], dtype=np.float64),
}

DEPARTURE_ROLE_PROPELLANT_BONUS = {
    "booster": {
        "lox_rp1": 1.12,
        "lox_ch4": 1.04,
        "lox_lh2": 0.84,
        "nto_mmh": 0.90,
        "hydrazine": 0.65,
    },
    "departure_mid": {
        "lox_rp1": 0.94,
        "lox_ch4": 1.02,
        "lox_lh2": 1.05,
        "nto_mmh": 0.96,
        "hydrazine": 0.70,
    },
    "departure_upper": {
        "lox_rp1": 0.88,
        "lox_ch4": 1.00,
        "lox_lh2": 1.12,
        "nto_mmh": 0.97,
        "hydrazine": 0.72,
    },
    "departure_core": {
        "lox_rp1": 1.08,
        "lox_ch4": 1.02,
        "lox_lh2": 0.90,
        "nto_mmh": 0.94,
        "hydrazine": 0.70,
    },
}

LONG_COAST_ARRIVAL_DV_CAP_MPS = {
    "lox_lh2": 250.0,
    "lox_ch4": 600.0,
    "lox_rp1": 750.0,
}

STANDARD_GRAVITY = 9.80665
INFEASIBLE_TANK_PENALTY_KG = 1_000_000_000.0
TANK_CARRY_PENALTY_SCALE = 0.35
LOW_GRAVITY_SURFACE_LAUNCH_THRESHOLD_MPS2 = 5.5
SURFACE_LAUNCH_ROLE_RELAXATION_PENALTY_KG = 180.0
SURFACE_LAUNCH_OVERTHRUST_PENALTY_SCALE_KG = 80.0
ENGINEERING_RE_RANK_RELATIVE_MARGIN = 0.02
ENGINEERING_RE_RANK_ABSOLUTE_MARGIN_KG = 150.0
SERVICE_STAGE_MAX_DV_MPS = 150.0
SERVICE_STAGE_MAX_DV_SHARE = 0.03
SERVICE_STAGE_MAX_TANK_MASS_KG = 100.0

KNOWN_BODY_SURFACE_GRAVITY_MPS2 = {
    "Mercury": 3.7024166712818314,
    "Venus": 8.870032755895648,
    "Earth": 9.820224436579496,
    "Mars": 3.727866112734456,
    "Jupiter": 25.92026389259008,
    "Saturn": 11.18595945778544,
    "Uranus": 9.007574112047987,
    "Neptune": 11.274524040433933,
    "Kerbin": 9.81,
    "Mun": 1.63,
    "Duna": 2.94,
}

SURFACE_LAUNCH_MAX_REASONABLE_TWR = {
    "booster": 2.50,
    "departure_core": 2.20,
    "departure_mid": 2.00,
    "departure_upper": 1.80,
}


@dataclass
class MissionRequest:
    dv_total_mps: float
    travel_days: float
    mean_distance_au: float
    min_distance_au: float
    payload_mass_kg: float
    origin: str | None = None
    destination: str | None = None
    dv_ejection_mps: float | None = None
    dv_injection_mps: float | None = None
    launch_ascent_dv_mps: float | None = None
    departure_orbit_km: float | None = None
    departure_orbit_periapsis_km: float | None = None
    departure_orbit_apoapsis_km: float | None = None
    arrival_orbit_km: float | None = None
    departure_distance_au: float | None = None
    arrival_distance_au: float | None = None
    departure_solar_flux_w_m2: float | None = None
    mean_solar_flux_w_m2: float | None = None
    phase_angle_deg: float | None = None
    transfer_angle_deg: float | None = None
    long_way: bool | None = None
    surface_launch: bool | None = None
    departure_surface_gravity_mps2: float | None = None
    correction_reserve_dv_mps: float | None = None


@dataclass
class StageMassEstimate:
    stage_number: int
    role: str
    segment: str
    payload_after_burn_kg: float
    storage_days: float
    solar_flux_relative: float
    engine_key: str
    engine_label: str
    engine_count: int
    engine_vacuum_isp_seconds: float
    engine_vacuum_thrust_kn: float
    available_thrust_kn: float
    loaded_propellant_mass_kg: float
    burn_propellant_mass_kg: float
    boiloff_mass_kg: float
    tank_mass_kg: float
    structure_mass_kg: float
    engine_dry_mass_kg: float
    dry_mass_kg: float
    operational_penalty_kg: float
    initial_mass_kg: float
    is_feasible: bool


@dataclass
class ArchitectureMassEstimate:
    stages: list[StageMassEstimate]
    estimated_launch_mass_kg: float
    estimated_engine_only_launch_mass_kg: float
    total_tank_mass_kg: float
    total_operational_penalty_kg: float
    tank_carry_mass_kg: float
    tank_carry_penalty_kg: float
    is_feasible: bool


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Predict the best rocket architecture for a mission.")
    parser.add_argument("--model-dir", required=True, help="Directory with model.pt and metadata.json.")
    parser.add_argument("--dv-total-mps", type=float)
    parser.add_argument("--travel-days", type=float)
    parser.add_argument("--solar-distance-au", type=float, help="Mean distance from the Sun during the mission in AU.")
    parser.add_argument("--min-distance-au", type=float, default=None, help="Optional minimum solar distance in AU.")
    parser.add_argument("--payload-mass-kg", type=float)
    parser.add_argument("--origin")
    parser.add_argument("--destination")
    parser.add_argument("--dv-ejection-mps", type=float)
    parser.add_argument("--dv-injection-mps", type=float)
    parser.add_argument("--launch-ascent-dv-mps", type=float)
    parser.add_argument("--departure-orbit-km", type=float)
    parser.add_argument("--departure-orbit-periapsis-km", type=float)
    parser.add_argument("--departure-orbit-apoapsis-km", type=float)
    parser.add_argument("--arrival-orbit-km", type=float)
    parser.add_argument("--departure-distance-au", type=float)
    parser.add_argument("--arrival-distance-au", type=float)
    parser.add_argument("--departure-solar-flux-w-m2", type=float)
    parser.add_argument("--mean-solar-flux-w-m2", type=float)
    parser.add_argument("--phase-angle-deg", type=float)
    parser.add_argument("--transfer-angle-deg", type=float)
    parser.add_argument("--long-way", action="store_true")
    parser.add_argument("--surface-launch", action="store_true")
    parser.add_argument("--departure-surface-gravity-mps2", type=float)
    parser.add_argument("--correction-reserve-dv-mps", type=float)
    parser.add_argument("--input-json", help="Optional JSON file with mission fields.")
    parser.add_argument("--output-json", help="Optional path to save the ranked candidates as JSON.")
    parser.add_argument("--device", default="auto", choices=["auto", "cpu", "cuda"])
    parser.add_argument("--batch-size", type=int, default=1024)
    parser.add_argument("--top-k", type=int, default=5)
    return parser.parse_args()


def select_device(requested: str) -> torch.device:
    if requested == "cpu":
        return torch.device("cpu")
    if requested == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA was requested, but torch.cuda.is_available() is False.")
        return torch.device("cuda")
    if torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


def load_request(args: argparse.Namespace) -> MissionRequest:
    payload: dict[str, object] = {}
    if args.input_json:
        payload.update(json.loads(Path(args.input_json).read_text(encoding="utf-8-sig")))

    direct_values = {
        "dv_total_mps": args.dv_total_mps,
        "travel_days": args.travel_days,
        "mean_distance_au": args.solar_distance_au,
        "min_distance_au": args.min_distance_au,
        "payload_mass_kg": args.payload_mass_kg,
        "origin": args.origin,
        "destination": args.destination,
        "dv_ejection_mps": args.dv_ejection_mps,
        "dv_injection_mps": args.dv_injection_mps,
        "launch_ascent_dv_mps": args.launch_ascent_dv_mps,
        "departure_orbit_km": args.departure_orbit_km,
        "departure_orbit_periapsis_km": args.departure_orbit_periapsis_km,
        "departure_orbit_apoapsis_km": args.departure_orbit_apoapsis_km,
        "arrival_orbit_km": args.arrival_orbit_km,
        "departure_distance_au": args.departure_distance_au,
        "arrival_distance_au": args.arrival_distance_au,
        "departure_solar_flux_w_m2": args.departure_solar_flux_w_m2,
        "mean_solar_flux_w_m2": args.mean_solar_flux_w_m2,
        "phase_angle_deg": args.phase_angle_deg,
        "transfer_angle_deg": args.transfer_angle_deg,
        "departure_surface_gravity_mps2": args.departure_surface_gravity_mps2,
        "correction_reserve_dv_mps": args.correction_reserve_dv_mps,
    }
    for key, value in direct_values.items():
        if value is not None:
            payload[key] = value
    if args.long_way:
        payload["long_way"] = True
    if args.surface_launch:
        payload["surface_launch"] = True

    required = ["dv_total_mps", "travel_days", "mean_distance_au", "payload_mass_kg"]
    missing = [key for key in required if key not in payload]
    if missing:
        raise ValueError(f"Missing mission fields: {', '.join(missing)}")

    return MissionRequest(
        dv_total_mps=float(payload["dv_total_mps"]),
        travel_days=float(payload["travel_days"]),
        mean_distance_au=float(payload["mean_distance_au"]),
        min_distance_au=float(payload.get("min_distance_au", payload["mean_distance_au"])),
        payload_mass_kg=float(payload["payload_mass_kg"]),
        origin=str(payload["origin"]) if "origin" in payload else None,
        destination=str(payload["destination"]) if "destination" in payload else None,
        dv_ejection_mps=float(payload["dv_ejection_mps"]) if "dv_ejection_mps" in payload else None,
        dv_injection_mps=float(payload["dv_injection_mps"]) if "dv_injection_mps" in payload else None,
        launch_ascent_dv_mps=float(payload["launch_ascent_dv_mps"]) if "launch_ascent_dv_mps" in payload else None,
        departure_orbit_km=float(payload["departure_orbit_km"]) if "departure_orbit_km" in payload else None,
        departure_orbit_periapsis_km=float(payload["departure_orbit_periapsis_km"]) if "departure_orbit_periapsis_km" in payload else None,
        departure_orbit_apoapsis_km=float(payload["departure_orbit_apoapsis_km"]) if "departure_orbit_apoapsis_km" in payload else None,
        arrival_orbit_km=float(payload["arrival_orbit_km"]) if "arrival_orbit_km" in payload else None,
        departure_distance_au=float(payload["departure_distance_au"]) if "departure_distance_au" in payload else None,
        arrival_distance_au=float(payload["arrival_distance_au"]) if "arrival_distance_au" in payload else None,
        departure_solar_flux_w_m2=float(payload["departure_solar_flux_w_m2"]) if "departure_solar_flux_w_m2" in payload else None,
        mean_solar_flux_w_m2=float(payload["mean_solar_flux_w_m2"]) if "mean_solar_flux_w_m2" in payload else None,
        phase_angle_deg=float(payload["phase_angle_deg"]) if "phase_angle_deg" in payload else None,
        transfer_angle_deg=float(payload["transfer_angle_deg"]) if "transfer_angle_deg" in payload else None,
        long_way=bool(payload["long_way"]) if "long_way" in payload else None,
        surface_launch=bool(payload["surface_launch"]) if "surface_launch" in payload else None,
        departure_surface_gravity_mps2=float(payload["departure_surface_gravity_mps2"]) if "departure_surface_gravity_mps2" in payload else None,
        correction_reserve_dv_mps=float(payload["correction_reserve_dv_mps"]) if "correction_reserve_dv_mps" in payload else None,
    )


def transform_request(request: MissionRequest) -> dict[str, float]:
    mean_distance = max(0.2, request.mean_distance_au)
    min_distance = max(0.2, request.min_distance_au)
    departure_distance = max(0.2, request.departure_distance_au if request.departure_distance_au is not None else request.mean_distance_au)
    arrival_distance = max(0.2, request.arrival_distance_au if request.arrival_distance_au is not None else request.mean_distance_au)
    dv_ejection = request.dv_ejection_mps
    dv_injection = request.dv_injection_mps
    total_dv = max(1.0, request.dv_total_mps)

    transformed = {
        "log_dv_total_mps": float(np.log1p(total_dv)),
        "log_travel_days": float(np.log1p(max(0.0, request.travel_days))),
        "log_payload_mass_kg": float(np.log1p(max(0.0, request.payload_mass_kg))),
        "departure_distance_au": float(departure_distance),
        "arrival_distance_au": float(arrival_distance),
        "mean_distance_au": float(request.mean_distance_au),
        "min_distance_au": float(request.min_distance_au),
        "min_solar_flux_relative": float(1.0 / (min_distance * min_distance)),
        "long_way_flag": float(bool(request.long_way)) if request.long_way is not None else None,
    }

    if dv_ejection is not None:
        transformed["log_dv_ejection_mps"] = float(np.log1p(max(0.0, dv_ejection)))
        transformed["ejection_dv_fraction"] = float(max(0.0, dv_ejection) / total_dv)
    if dv_injection is not None:
        transformed["log_dv_injection_mps"] = float(np.log1p(max(0.0, dv_injection)))
        transformed["injection_dv_fraction"] = float(max(0.0, dv_injection) / total_dv)
    departure_orbit_km = request.departure_orbit_km
    if departure_orbit_km is None:
        departure_orbit_km = request.departure_orbit_periapsis_km
    if departure_orbit_km is not None:
        transformed["log_departure_orbit_km"] = float(np.log1p(max(1.0, departure_orbit_km)))
    if request.arrival_orbit_km is not None:
        transformed["log_arrival_orbit_km"] = float(np.log1p(max(1.0, request.arrival_orbit_km)))
    if request.departure_solar_flux_w_m2 is not None:
        transformed["departure_solar_flux_relative"] = float(max(1.0, request.departure_solar_flux_w_m2) / 1361.0)
    if request.mean_solar_flux_w_m2 is not None:
        transformed["mean_solar_flux_relative"] = float(max(1.0, request.mean_solar_flux_w_m2) / 1361.0)
    else:
        transformed["mean_solar_flux_relative"] = float(1.0 / (mean_distance * mean_distance))
    if request.phase_angle_deg is not None:
        phase_rad = np.deg2rad(request.phase_angle_deg)
        transformed["phase_angle_sin"] = float(np.sin(phase_rad))
        transformed["phase_angle_cos"] = float(np.cos(phase_rad))
    if request.transfer_angle_deg is not None:
        transfer_rad = np.deg2rad(request.transfer_angle_deg)
        transformed["transfer_angle_sin"] = float(np.sin(transfer_rad))
        transformed["transfer_angle_cos"] = float(np.cos(transfer_rad))
    if request.correction_reserve_dv_mps is not None:
        transformed["log_correction_reserve_dv_mps"] = float(np.log1p(max(0.0, request.correction_reserve_dv_mps)))

    return {key: value for key, value in transformed.items() if value is not None}


def resolve_category_id(value: str | None, vocab: list[str]) -> int:
    lookup = {entry: index for index, entry in enumerate(vocab)}
    if value is None:
        return lookup.get(UNKNOWN_TOKEN, 0)
    return lookup.get(value, lookup.get(UNKNOWN_TOKEN, 0))


def stage_roles_for_count(stage_count: int) -> list[tuple[str, str]]:
    departure_stage_count = stage_count - 1
    stages: list[tuple[str, str]] = []
    for departure_index in range(departure_stage_count):
        if departure_stage_count == 1:
            role = "departure_core"
        elif departure_index == 0:
            role = "booster"
        elif departure_index == departure_stage_count - 1:
            role = "departure_upper"
        else:
            role = "departure_mid"
        stages.append((role, "Departure"))
    stages.append(("cruise_arrival", "Arrival"))
    return stages


def stage_engine_specs_for_architecture(propellants: list[str], stage_count: int) -> list[EngineSpec | None]:
    roles = stage_roles_for_count(stage_count)
    engine_specs: list[EngineSpec | None] = []
    for index in range(stage_count):
        role, segment = roles[index]
        engine_specs.append(select_engine_for_stage(propellants[index], role, segment))
    return engine_specs


def stage_engines_for_architecture(propellants: list[str], stage_count: int) -> list[str]:
    return [engine.key if engine is not None else ENGINE_PAD_TOKEN for engine in stage_engine_specs_for_architecture(propellants, stage_count)]


def stage_numeric_features_for_architecture(
    request: MissionRequest,
    propellants: list[str],
    engine_specs: list[EngineSpec | None],
    max_stage_count: int,
) -> list[float]:
    segments = [segment for _, segment in stage_roles_for_count(len(propellants))]
    return build_stage_numeric_feature_vector(
        travel_days=request.travel_days,
        mean_distance_au=request.mean_distance_au,
        departure_distance_au=request.departure_distance_au,
        departure_solar_flux_w_m2=request.departure_solar_flux_w_m2,
        mean_solar_flux_w_m2=request.mean_solar_flux_w_m2,
        propellant_keys=propellants,
        engine_specs=engine_specs,
        segments=segments,
        max_stage_count=max_stage_count,
        departure_storage_days=DEFAULT_DEPARTURE_STORAGE_DAYS,
    )


def departure_storage_days_for_stage(departure_stage_count: int, departure_index: int) -> float:
    storage_fractions = (
        (0.22,),
        (0.08, 0.28),
        (0.05, 0.12, 0.24),
        (0.04, 0.08, 0.14, 0.22),
    )
    fractions = storage_fractions[departure_stage_count - 1]
    return max(0.2, DEFAULT_DEPARTURE_STORAGE_DAYS * fractions[departure_index])


def resolve_stage_storage_days(request: MissionRequest, segment: str, departure_stage_count: int, departure_index: int) -> float:
    if segment == "Arrival":
        return max(0.2, request.travel_days)
    return departure_storage_days_for_stage(departure_stage_count, departure_index)


def resolve_stage_solar_flux_relative(request: MissionRequest, segment: str) -> float:
    if segment == "Arrival":
        if request.mean_solar_flux_w_m2 is not None:
            return max(1.0, request.mean_solar_flux_w_m2) / 1361.0
        return 1.0 / math.pow(max(0.2, request.mean_distance_au), 2)

    if request.departure_solar_flux_w_m2 is not None:
        return max(1.0, request.departure_solar_flux_w_m2) / 1361.0
    departure_distance = request.departure_distance_au if request.departure_distance_au is not None else request.mean_distance_au
    return 1.0 / math.pow(max(0.2, departure_distance), 2)


def stage_fixed_service_mass_kg(role: str, segment: str) -> float:
    if segment == "Arrival":
        return 85.0
    if role == "booster":
        return 320.0
    if role == "departure_core":
        return 280.0
    if role == "departure_mid":
        return 190.0
    if role == "departure_upper":
        return 140.0
    return 180.0


def stage_structure_factor(role: str, segment: str) -> float:
    if segment == "Arrival":
        return 0.010
    if role == "booster":
        return 0.018
    if role == "departure_mid":
        return 0.015
    if role == "departure_upper":
        return 0.012
    return 0.016


def compute_stage_structure_mass_kg(payload_after_burn_kg: float, role: str, segment: str) -> float:
    scaled_mass_kg = payload_after_burn_kg * stage_structure_factor(role, segment)
    return scaled_mass_kg + stage_fixed_service_mass_kg(role, segment)


def resolve_departure_surface_gravity_mps2(request: MissionRequest) -> float:
    explicit = request.departure_surface_gravity_mps2
    if explicit is not None and math.isfinite(explicit) and explicit > 0.0:
        return float(explicit)

    if request.origin:
        known = KNOWN_BODY_SURFACE_GRAVITY_MPS2.get(request.origin)
        if known is not None:
            return float(known)

    return STANDARD_GRAVITY


def resolve_stage_target_acceleration_ratio(role: str, segment: str) -> float:
    if segment == "Arrival":
        return 0.35
    if role == "booster":
        return 1.10
    if role == "departure_core":
        return 1.00
    if role == "departure_mid":
        return 0.75
    if role == "departure_upper":
        return 0.45
    return 0.60


def resolve_stage_target_acceleration_mps2(request: MissionRequest, role: str, segment: str) -> float:
    reference_gravity = STANDARD_GRAVITY
    if segment == "Departure" and is_surface_launch_request(request):
        reference_gravity = resolve_departure_surface_gravity_mps2(request)
    return reference_gravity * resolve_stage_target_acceleration_ratio(role, segment)


def resolve_stage_required_thrust_kn(request: MissionRequest, role: str, segment: str, initial_mass_kg: float) -> float:
    return initial_mass_kg * resolve_stage_target_acceleration_mps2(request, role, segment) / 1000.0


def resolve_engine_count(request: MissionRequest, role: str, segment: str, initial_mass_kg: float, engine: EngineSpec) -> int:
    required_thrust_kn = resolve_stage_required_thrust_kn(request, role, segment, initial_mass_kg)
    return max(1, int(math.ceil(required_thrust_kn / max(1.0, engine.vacuum_thrust_kn))))


def should_relax_surface_launch_engine_roles(request: MissionRequest, role: str, segment: str) -> bool:
    return (
        segment == "Departure"
        and role in {"booster", "departure_core"}
        and is_surface_launch_request(request)
        and resolve_departure_surface_gravity_mps2(request) <= LOW_GRAVITY_SURFACE_LAUNCH_THRESHOLD_MPS2
    )


def stage_engine_candidates(
    request: MissionRequest,
    propellant_key: str,
    role: str,
    segment: str,
    preferred_engine: EngineSpec | None,
) -> list[EngineSpec]:
    if propellant_key in {"none", ""}:
        return []

    candidates: list[EngineSpec] = []
    if preferred_engine is not None:
        candidates.append(preferred_engine)

    if not should_relax_surface_launch_engine_roles(request, role, segment):
        return deduplicate_engines(candidates)

    relaxed_candidates = sorted(
        (
            engine
            for engine in load_engine_catalog()
            if engine.propellant_key == propellant_key and engine.supports_segment(segment)
        ),
        key=lambda item: (item.vacuum_thrust_kn, item.dry_mass_kg, -item.vacuum_isp_seconds, item.selection_rank),
    )
    candidates.extend(relaxed_candidates)
    return deduplicate_engines(candidates)


def deduplicate_engines(candidates: list[EngineSpec]) -> list[EngineSpec]:
    unique: list[EngineSpec] = []
    seen: set[str] = set()
    for engine in candidates:
        if engine.key in seen:
            continue
        seen.add(engine.key)
        unique.append(engine)
    return unique


def compute_stage_surface_launch_thrust_penalty_kg(request: MissionRequest, stage: StageMassEstimate) -> float:
    if not stage.is_feasible or stage.segment != "Departure" or not is_surface_launch_request(request):
        return 0.0

    surface_gravity = resolve_departure_surface_gravity_mps2(request)
    if surface_gravity <= 1e-9:
        return 0.0

    max_reasonable_twr = SURFACE_LAUNCH_MAX_REASONABLE_TWR.get(stage.role)
    if max_reasonable_twr is None or max_reasonable_twr <= 0.0:
        return 0.0

    actual_twr = (stage.available_thrust_kn * 1000.0) / max(1.0, stage.initial_mass_kg * surface_gravity)
    excess_ratio = (actual_twr / max_reasonable_twr) - 1.0
    if excess_ratio <= 0.0:
        return 0.0

    return float(SURFACE_LAUNCH_OVERTHRUST_PENALTY_SCALE_KG * excess_ratio * excess_ratio)


def compute_surface_launch_thrust_penalty_kg(request: MissionRequest, stages: list[StageMassEstimate]) -> float:
    return float(sum(compute_stage_surface_launch_thrust_penalty_kg(request, stage) for stage in stages))


def compute_stage_operational_penalty_kg(
    request: MissionRequest,
    propellant_key: str,
    engine: EngineSpec,
    segment: str,
    initial_mass_kg: float,
    storage_days: float,
    solar_flux_relative: float,
    delta_v_mps: float,
    engine_count: int,
) -> float:
    propellant = get_propellant_spec(propellant_key)
    if propellant is None or not math.isfinite(initial_mass_kg) or initial_mass_kg <= 0.0:
        return 0.0

    penalty = initial_mass_kg * propellant.handling_penalty_factor
    if segment == "Departure":
        penalty += (
            initial_mass_kg
            * propellant.primary_burn_penalty_factor
            * max(0.35, min(1.5, delta_v_mps / 3500.0))
        )
        if propellant.is_low_thrust and (delta_v_mps > 1200.0 or request.payload_mass_kg > 3000.0):
            penalty += initial_mass_kg * 0.35
    else:
        penalty += (
            initial_mass_kg
            * propellant.long_coast_penalty_factor
            * max(0.25, min(2.0, storage_days / 180.0))
            * max(0.5, solar_flux_relative)
        )
        penalty += initial_mass_kg * propellant.restart_penalty_factor
        if not engine.supports_long_coast:
            penalty += initial_mass_kg * 0.06

    if propellant.is_hypergolic:
        penalty += initial_mass_kg * 0.01
    if segment == "Arrival" and not engine.supports_restart:
        penalty += initial_mass_kg * 0.08
    if engine_count > 1:
        penalty += initial_mass_kg * min(0.03, (engine_count - 1) * 0.0025)
    if propellant.is_cryogenic and segment == "Arrival" and request.min_distance_au < 1.0:
        penalty += initial_mass_kg * (1.0 - request.min_distance_au) * 0.10
    return float(penalty)


def score_stage_engine_candidate(request: MissionRequest, estimate: StageMassEstimate, engine: EngineSpec) -> float:
    if not estimate.is_feasible:
        return float("inf")

    score = float(estimate.initial_mass_kg)
    score += float(estimate.operational_penalty_kg)
    score += compute_stage_surface_launch_thrust_penalty_kg(request, estimate)
    if not engine.supports_role(estimate.role):
        score += SURFACE_LAUNCH_ROLE_RELAXATION_PENALTY_KG
    return score


def estimate_stage_mass_for_engine(
    request: MissionRequest,
    stage_number: int,
    role: str,
    segment: str,
    propellant_key: str,
    delta_v_mps: float,
    payload_after_burn_kg: float,
    engine: EngineSpec | None,
    storage_days: float,
    solar_flux_relative: float,
    *,
    include_tank_mass: bool,
) -> StageMassEstimate:
    propellant = get_propellant_spec(propellant_key)

    if engine is None or propellant is None or not math.isfinite(payload_after_burn_kg) or payload_after_burn_kg <= 0.0:
        return StageMassEstimate(
            stage_number=stage_number,
            role=role,
            segment=segment,
            payload_after_burn_kg=payload_after_burn_kg,
            storage_days=storage_days,
            solar_flux_relative=solar_flux_relative,
            engine_key=engine.key if engine is not None else ENGINE_PAD_TOKEN,
            engine_label=engine.display_name if engine is not None else ENGINE_LABELS.get(ENGINE_PAD_TOKEN, ENGINE_PAD_TOKEN),
            engine_count=0,
            engine_vacuum_isp_seconds=engine.vacuum_isp_seconds if engine is not None else 0.0,
            engine_vacuum_thrust_kn=engine.vacuum_thrust_kn if engine is not None else 0.0,
            available_thrust_kn=0.0,
            loaded_propellant_mass_kg=0.0,
            burn_propellant_mass_kg=0.0,
            boiloff_mass_kg=0.0,
            tank_mass_kg=0.0,
            structure_mass_kg=0.0,
            engine_dry_mass_kg=0.0,
            dry_mass_kg=0.0,
            operational_penalty_kg=0.0,
            initial_mass_kg=INFEASIBLE_TANK_PENALTY_KG,
            is_feasible=False,
        )

    effective_tankage_factor = propellant.effective_tankage_factor(storage_days) if include_tank_mass else 0.0
    boiloff_fraction = propellant.compute_boiloff_fraction(storage_days, solar_flux_relative)
    structure_mass_kg = compute_stage_structure_mass_kg(payload_after_burn_kg, role, segment)
    engine_count = 1
    mass_ratio = math.exp(max(0.0, delta_v_mps) / (STANDARD_GRAVITY * max(1.0, engine.vacuum_isp_seconds)))
    available_thrust_kn = 0.0
    loaded_propellant_mass_kg = 0.0
    burn_propellant_mass_kg = 0.0
    boiloff_mass_kg = 0.0
    tank_mass_kg = 0.0
    engine_dry_mass_kg = 0.0
    dry_mass_kg = 0.0
    initial_mass_kg = INFEASIBLE_TANK_PENALTY_KG

    denominator = (1.0 - boiloff_fraction) + effective_tankage_factor * (1.0 - mass_ratio)
    if denominator <= 1e-9:
        return StageMassEstimate(
            stage_number=stage_number,
            role=role,
            segment=segment,
            payload_after_burn_kg=payload_after_burn_kg,
            storage_days=storage_days,
            solar_flux_relative=solar_flux_relative,
            engine_key=engine.key,
            engine_label=engine.display_name,
            engine_count=0,
            engine_vacuum_isp_seconds=engine.vacuum_isp_seconds,
            engine_vacuum_thrust_kn=engine.vacuum_thrust_kn,
            available_thrust_kn=0.0,
            loaded_propellant_mass_kg=0.0,
            burn_propellant_mass_kg=0.0,
            boiloff_mass_kg=0.0,
            tank_mass_kg=0.0,
            structure_mass_kg=structure_mass_kg,
            engine_dry_mass_kg=0.0,
            dry_mass_kg=0.0,
            operational_penalty_kg=0.0,
            initial_mass_kg=INFEASIBLE_TANK_PENALTY_KG,
            is_feasible=False,
        )

    for _ in range(12):
        engine_dry_mass_kg = engine_count * engine.dry_mass_kg
        fixed_dry_mass_kg = structure_mass_kg + engine_dry_mass_kg
        loaded_propellant_mass_kg = (payload_after_burn_kg + fixed_dry_mass_kg) * (mass_ratio - 1.0) / denominator
        if not math.isfinite(loaded_propellant_mass_kg) or loaded_propellant_mass_kg <= 0.0:
            break

        boiloff_mass_kg = loaded_propellant_mass_kg * boiloff_fraction
        burn_propellant_mass_kg = loaded_propellant_mass_kg - boiloff_mass_kg
        tank_mass_kg = loaded_propellant_mass_kg * effective_tankage_factor
        dry_mass_kg = fixed_dry_mass_kg + tank_mass_kg
        initial_mass_kg = payload_after_burn_kg + dry_mass_kg + loaded_propellant_mass_kg
        available_thrust_kn = engine_count * engine.vacuum_thrust_kn

        required_engine_count = resolve_engine_count(request, role, segment, initial_mass_kg, engine)
        if required_engine_count > engine.max_cluster_count:
            break
        if required_engine_count == engine_count:
            return StageMassEstimate(
                stage_number=stage_number,
                role=role,
                segment=segment,
                payload_after_burn_kg=payload_after_burn_kg,
                storage_days=storage_days,
                solar_flux_relative=solar_flux_relative,
                engine_key=engine.key,
                engine_label=engine.display_name,
                engine_count=engine_count,
                engine_vacuum_isp_seconds=engine.vacuum_isp_seconds,
                engine_vacuum_thrust_kn=engine.vacuum_thrust_kn,
                available_thrust_kn=available_thrust_kn,
                loaded_propellant_mass_kg=loaded_propellant_mass_kg,
                burn_propellant_mass_kg=burn_propellant_mass_kg,
                boiloff_mass_kg=boiloff_mass_kg,
                tank_mass_kg=tank_mass_kg,
                structure_mass_kg=structure_mass_kg,
                engine_dry_mass_kg=engine_dry_mass_kg,
                dry_mass_kg=dry_mass_kg,
                operational_penalty_kg=compute_stage_operational_penalty_kg(
                    request,
                    propellant_key,
                    engine,
                    segment,
                    initial_mass_kg,
                    storage_days,
                    solar_flux_relative,
                    delta_v_mps,
                    engine_count,
                ),
                initial_mass_kg=initial_mass_kg,
                is_feasible=True,
            )
        engine_count = required_engine_count

    return StageMassEstimate(
        stage_number=stage_number,
        role=role,
        segment=segment,
        payload_after_burn_kg=payload_after_burn_kg,
        storage_days=storage_days,
        solar_flux_relative=solar_flux_relative,
        engine_key=engine.key,
        engine_label=engine.display_name,
        engine_count=engine_count,
        engine_vacuum_isp_seconds=engine.vacuum_isp_seconds,
        engine_vacuum_thrust_kn=engine.vacuum_thrust_kn,
        available_thrust_kn=available_thrust_kn,
        loaded_propellant_mass_kg=max(0.0, loaded_propellant_mass_kg),
        burn_propellant_mass_kg=max(0.0, burn_propellant_mass_kg),
        boiloff_mass_kg=max(0.0, boiloff_mass_kg),
        tank_mass_kg=max(0.0, tank_mass_kg),
        structure_mass_kg=structure_mass_kg,
        engine_dry_mass_kg=max(0.0, engine_dry_mass_kg),
        dry_mass_kg=max(0.0, dry_mass_kg),
        operational_penalty_kg=0.0,
        initial_mass_kg=INFEASIBLE_TANK_PENALTY_KG,
        is_feasible=False,
    )


def estimate_stage_mass(
    request: MissionRequest,
    stage_number: int,
    role: str,
    segment: str,
    propellant_key: str,
    delta_v_mps: float,
    payload_after_burn_kg: float,
    engine: EngineSpec | None,
    storage_days: float,
    solar_flux_relative: float,
    *,
    include_tank_mass: bool,
) -> StageMassEstimate:
    candidates = stage_engine_candidates(request, propellant_key, role, segment, engine)
    if not candidates:
        return estimate_stage_mass_for_engine(
            request,
            stage_number,
            role,
            segment,
            propellant_key,
            delta_v_mps,
            payload_after_burn_kg,
            engine,
            storage_days,
            solar_flux_relative,
            include_tank_mass=include_tank_mass,
        )

    best_estimate: StageMassEstimate | None = None
    best_score = float("inf")
    fallback_estimate: StageMassEstimate | None = None

    for candidate in candidates:
        estimate = estimate_stage_mass_for_engine(
            request,
            stage_number,
            role,
            segment,
            propellant_key,
            delta_v_mps,
            payload_after_burn_kg,
            candidate,
            storage_days,
            solar_flux_relative,
            include_tank_mass=include_tank_mass,
        )
        if fallback_estimate is None:
            fallback_estimate = estimate

        score = score_stage_engine_candidate(request, estimate, candidate)
        if score < best_score:
            best_score = score
            best_estimate = estimate

    return best_estimate or fallback_estimate or estimate_stage_mass_for_engine(
        request,
        stage_number,
        role,
        segment,
        propellant_key,
        delta_v_mps,
        payload_after_burn_kg,
        engine,
        storage_days,
        solar_flux_relative,
        include_tank_mass=include_tank_mass,
    )


def estimate_architecture_stack(
    request: MissionRequest,
    propellants: list[str],
    stage_shares: np.ndarray,
    engine_specs: list[EngineSpec | None],
    *,
    include_tank_mass: bool,
) -> tuple[list[StageMassEstimate], float, bool]:
    roles = stage_roles_for_count(len(propellants))
    allocation_total_dv_mps = resolve_stage_allocation_total_dv(request)
    stage_estimates: list[StageMassEstimate | None] = [None] * len(propellants)
    payload_after_burn_kg = max(1.0, request.payload_mass_kg)
    departure_stage_count = max(1, len(propellants) - 1)

    for index in range(len(propellants) - 1, -1, -1):
        role, segment = roles[index]
        storage_days = resolve_stage_storage_days(request, segment, departure_stage_count, index)
        solar_flux_relative = resolve_stage_solar_flux_relative(request, segment)
        engine = engine_specs[index]
        estimate = estimate_stage_mass(
            request,
            stage_number=index + 1,
            role=role,
            segment=segment,
            propellant_key=propellants[index],
            delta_v_mps=float(stage_shares[index] * allocation_total_dv_mps),
            payload_after_burn_kg=payload_after_burn_kg,
            engine=engine,
            storage_days=storage_days,
            solar_flux_relative=solar_flux_relative,
            include_tank_mass=include_tank_mass,
        )
        stage_estimates[index] = estimate
        payload_after_burn_kg = estimate.initial_mass_kg

    stages = [estimate for estimate in stage_estimates if estimate is not None]
    feasible = all(stage.is_feasible for stage in stages) and math.isfinite(stages[0].initial_mass_kg)
    launch_mass_kg = stages[0].initial_mass_kg if feasible else INFEASIBLE_TANK_PENALTY_KG
    return stages, launch_mass_kg, feasible


def estimate_architecture_mass_penalty(
    request: MissionRequest,
    propellants: list[str],
    stage_shares: np.ndarray,
    engine_specs: list[EngineSpec | None],
    *,
    tank_carry_penalty_scale: float = TANK_CARRY_PENALTY_SCALE,
) -> ArchitectureMassEstimate:
    normalized_stage_shares = normalize_stage_shares(np.asarray(stage_shares, dtype=np.float64))
    stages_with_tanks, launch_mass_with_tanks_kg, feasible_with_tanks = estimate_architecture_stack(
        request,
        propellants,
        normalized_stage_shares,
        engine_specs,
        include_tank_mass=True,
    )
    _, launch_mass_engine_only_kg, feasible_engine_only = estimate_architecture_stack(
        request,
        propellants,
        normalized_stage_shares,
        engine_specs,
        include_tank_mass=False,
    )

    total_tank_mass_kg = float(sum(stage.tank_mass_kg for stage in stages_with_tanks))
    total_operational_penalty_kg = float(sum(stage.operational_penalty_kg for stage in stages_with_tanks))
    if feasible_with_tanks and feasible_engine_only:
        tank_carry_mass_kg = max(0.0, launch_mass_with_tanks_kg - launch_mass_engine_only_kg)
    else:
        tank_carry_mass_kg = INFEASIBLE_TANK_PENALTY_KG

    return ArchitectureMassEstimate(
        stages=stages_with_tanks,
        estimated_launch_mass_kg=float(launch_mass_with_tanks_kg),
        estimated_engine_only_launch_mass_kg=float(launch_mass_engine_only_kg),
        total_tank_mass_kg=total_tank_mass_kg,
        total_operational_penalty_kg=total_operational_penalty_kg,
        tank_carry_mass_kg=float(tank_carry_mass_kg),
        tank_carry_penalty_kg=float(tank_carry_mass_kg * tank_carry_penalty_scale),
        is_feasible=feasible_with_tanks and feasible_engine_only,
    )


def compute_tank_carry_penalty_kg(
    request: MissionRequest,
    propellants: list[str],
    stage_shares: np.ndarray | list[float],
    *,
    tank_carry_penalty_scale: float = TANK_CARRY_PENALTY_SCALE,
) -> float:
    engine_specs = stage_engine_specs_for_architecture(propellants, len(propellants))
    estimate = estimate_architecture_mass_penalty(
        request,
        propellants,
        np.asarray(stage_shares, dtype=np.float64),
        engine_specs,
        tank_carry_penalty_scale=tank_carry_penalty_scale,
    )
    return estimate.tank_carry_penalty_kg


def resolve_tank_carry_penalty_scale(metadata: ModelMetadata) -> float:
    return 0.16 if metadata.feature_version >= 6 else TANK_CARRY_PENALTY_SCALE


def resolve_engineering_rerank_margin_kg(best_estimated_score_kg: float) -> float:
    return max(
        ENGINEERING_RE_RANK_ABSOLUTE_MARGIN_KG,
        best_estimated_score_kg * ENGINEERING_RE_RANK_RELATIVE_MARGIN,
    )


def engineering_rerank_sort_key(item: dict[str, object]) -> tuple[object, ...]:
    effective_stage_count = int(item.get("effective_stage_count", item["stage_count"]))
    if bool(item.get("engineering_close_to_best")):
        return (
            0,
            effective_stage_count,
            int(item["stage_count"]),
            float(item["estimated_total_score_kg_equivalent"]),
            float(item["estimated_total_tank_mass_kg"]),
            float(item["predicted_score_kg_equivalent"]),
            float(item["predicted_raw_score_kg_equivalent"]),
            str(item["architecture_key"]),
        )

    return (
        1,
        float(item["predicted_score_kg_equivalent"]),
        float(item["estimated_total_score_kg_equivalent"]),
        effective_stage_count,
        int(item["stage_count"]),
        float(item["estimated_total_tank_mass_kg"]),
        str(item["architecture_key"]),
    )


def apply_engineering_rerank(results: list[dict[str, object]]) -> list[dict[str, object]]:
    if not results:
        return results

    finite_estimated_scores = [
        float(item["estimated_total_score_kg_equivalent"])
        for item in results
        if math.isfinite(float(item["estimated_total_score_kg_equivalent"]))
    ]
    if not finite_estimated_scores:
        results.sort(key=lambda item: item["predicted_score_kg_equivalent"])
        return results

    best_estimated_score_kg = min(finite_estimated_scores)
    engineering_margin_kg = resolve_engineering_rerank_margin_kg(best_estimated_score_kg)
    for item in results:
        estimated_score = float(item["estimated_total_score_kg_equivalent"])
        engineering_close = math.isfinite(estimated_score) and estimated_score <= (best_estimated_score_kg + engineering_margin_kg)
        item["engineering_close_to_best"] = engineering_close
        item["engineering_margin_kg"] = float(engineering_margin_kg)
        item["selected_by_engineering_margin"] = False

    results.sort(key=engineering_rerank_sort_key)
    if results:
        best_item = results[0]
        best_item["selected_by_engineering_margin"] = bool(
            best_item.get("engineering_close_to_best")
            and float(best_item["estimated_total_score_kg_equivalent"]) > (best_estimated_score_kg + 1e-6)
        )
    return results


def normalize_stage_shares(values: np.ndarray) -> np.ndarray:
    clipped = np.clip(values.astype(np.float64, copy=False), 0.0, None)
    total = float(clipped.sum())
    if total <= 1e-9:
        return np.full_like(clipped, 1.0 / max(1, clipped.size))
    return clipped / total


def resolve_stage_allocation_total_dv(request: MissionRequest) -> float:
    reserve = max(0.0, request.correction_reserve_dv_mps or 0.0)
    return max(1.0, request.dv_total_mps + reserve)


def is_surface_launch_request(request: MissionRequest) -> bool:
    return is_surface_launch_context(request.surface_launch, request.launch_ascent_dv_mps)


def compute_stage_complexity_prior_kg(request: MissionRequest, stage_count: int) -> float:
    """
    Compute complexity penalty based on stage count, with mission-aware adjustment.
    
    Aggressively penalize 3+ stages for low-ΔV missions.
    Stage 2 (baseline): no penalty (0 kg)
    Stage 3:
      - ΔV < 11,000 m/s (Mars/Moon): +1,200 kg (very strong penalty)
      - ΔV 11,000-13,000 m/s (Jupiter): +600 kg (strong penalty)
      - ΔV > 13,000 m/s (Saturn/Neptune): +300 kg (moderate penalty)
    Stage 4+: exponential penalty 2,000 kg per extra stage
    """
    if stage_count <= 2:
        if is_surface_launch_request(request):
            ascent_dv = max(0.0, request.launch_ascent_dv_mps or 0.0)
            return float(np.clip(250.0 + (0.18 * ascent_dv), 300.0, 1_250.0))
        return 0.0
    
    penalty_kg = 0.0
    
    # Stage 3 penalty (mission-aware based on ΔV)
    if stage_count >= 3:
        if request.dv_total_mps < 11_000:
            # Mars, Moon, etc. - very strong penalty to force 2-stage
            stage_3_penalty = 1_200.0
        elif request.dv_total_mps < 13_000:
            # Jupiter - strong penalty, prefer 2 stages
            stage_3_penalty = 600.0
        else:
            # Saturn, Neptune, etc. - moderate penalty for 3+ stages
            stage_3_penalty = 300.0
        if is_surface_launch_request(request):
            stage_3_penalty *= 0.45
        penalty_kg += stage_3_penalty
    
    # Stage 4+ penalty (exponential, much more aggressive)
    if stage_count >= 4:
        penalty_kg += 2_000.0 * (stage_count - 3)
    
    return float(penalty_kg)


def compute_stage_complexity_prior_kg(request: MissionRequest, stage_count: int) -> float:
    return compute_stage_complexity_prior_from_values(
        request.dv_total_mps,
        stage_count,
        surface_launch=request.surface_launch,
        launch_ascent_dv_mps=request.launch_ascent_dv_mps,
    )


def compute_architecture_realism_penalty_kg(request: MissionRequest, propellants: list[str]) -> float:
    return compute_architecture_realism_penalty_from_values(
        propellants,
        dv_total_mps=request.dv_total_mps,
        travel_days=request.travel_days,
        min_distance_au=request.min_distance_au,
        surface_launch=request.surface_launch,
        launch_ascent_dv_mps=request.launch_ascent_dv_mps,
    )


def constrain_arrival_stage_share(
    request: MissionRequest,
    arrival_propellant: str,
    arrival_share: float,
) -> float:
    has_explicit_arrival_split = request.dv_ejection_mps is not None or request.dv_injection_mps is not None
    if has_explicit_arrival_split or request.travel_days < 180:
        return float(np.clip(arrival_share, 0.0, 1.0))

    base_cap_dv = LONG_COAST_ARRIVAL_DV_CAP_MPS.get(arrival_propellant)
    if base_cap_dv is None:
        return float(np.clip(arrival_share, 0.0, 1.0))

    duration_scale = 1.0
    if request.travel_days >= 360:
        duration_scale = 0.60
    elif request.travel_days >= 240:
        duration_scale = 0.80

    thermal_scale = 0.85 if request.mean_distance_au <= 0.85 else 1.0
    reserve = max(0.0, request.correction_reserve_dv_mps or 0.0)
    capped_arrival_dv = reserve + (base_cap_dv * duration_scale * thermal_scale)
    capped_arrival_share = capped_arrival_dv / resolve_stage_allocation_total_dv(request)
    return float(np.clip(min(arrival_share, capped_arrival_share), 0.0, 1.0))


def estimate_arrival_stage_share(request: MissionRequest, arrival_propellant: str) -> float:
    reserve = max(0.0, request.correction_reserve_dv_mps or 0.0)
    if request.dv_ejection_mps is not None or request.dv_injection_mps is not None:
        if request.dv_ejection_mps is not None:
            ejection = max(0.0, request.dv_ejection_mps)
            injection = max(0.0, request.dv_injection_mps if request.dv_injection_mps is not None else request.dv_total_mps - ejection)
        else:
            injection = max(0.0, request.dv_injection_mps or 0.0)
            ejection = max(0.0, request.dv_total_mps - injection)
        effective_total = max(1.0, ejection + injection + reserve)
        return float(np.clip((injection + reserve) / effective_total, 0.0, 1.0))

    share = 0.18
    if request.travel_days >= 120:
        share += 0.04
    if request.travel_days >= 240:
        share += 0.04
    if request.mean_distance_au <= 0.85:
        share += 0.03
    if arrival_propellant in {"nto_mmh", "hydrazine"}:
        share += 0.05
    elif arrival_propellant == "lox_ch4":
        share += 0.01
    elif arrival_propellant == "lox_lh2":
        share -= 0.02

    share += reserve / resolve_stage_allocation_total_dv(request)
    share = float(np.clip(share, 0.12, 0.42))
    return constrain_arrival_stage_share(request, arrival_propellant, share)


def allocate_stage_dv_shares_fallback(request: MissionRequest, propellants: list[str]) -> np.ndarray:
    stage_count = len(propellants)
    if stage_count <= 1:
        return np.asarray([1.0], dtype=np.float64)

    roles = stage_roles_for_count(stage_count)
    departure_propellants = propellants[:-1]
    departure_count = len(departure_propellants)
    template = DEPARTURE_SHARE_TEMPLATES[departure_count]
    arrival_share = estimate_arrival_stage_share(request, propellants[-1])
    departure_total_share = max(0.0, 1.0 - arrival_share)

    departure_weights = []
    for index, propellant in enumerate(departure_propellants):
        role, _ = roles[index]
        role_bonus = DEPARTURE_ROLE_PROPELLANT_BONUS.get(role, {})
        departure_weights.append(template[index] * role_bonus.get(propellant, 1.0))

    normalized_departure = normalize_stage_shares(np.asarray(departure_weights, dtype=np.float64))
    return np.concatenate([normalized_departure * departure_total_share, np.asarray([arrival_share], dtype=np.float64)])


def allocate_stage_dv_shares_hybrid(
    request: MissionRequest,
    propellants: list[str],
    ml_stage_shares: np.ndarray | None,
) -> tuple[np.ndarray, str]:
    heuristic_shares = allocate_stage_dv_shares_fallback(request, propellants)
    if ml_stage_shares is None:
        return heuristic_shares, "physics_heuristic"

    stage_count = len(propellants)
    if stage_count <= 1:
        return np.asarray([1.0], dtype=np.float64), "physics_heuristic"

    ml_shares = normalize_stage_shares(np.asarray(ml_stage_shares[:stage_count], dtype=np.float64))
    arrival_share = float(heuristic_shares[-1])
    arrival_propellant = propellants[-1]

    departure_total_share = max(0.0, 1.0 - arrival_share)
    heuristic_departure = normalize_stage_shares(heuristic_shares[:-1])
    ml_departure = normalize_stage_shares(ml_shares[:-1])
    blended_departure = normalize_stage_shares((0.70 * heuristic_departure) + (0.30 * ml_departure))

    if request.travel_days >= 120 and arrival_propellant in {"nto_mmh", "hydrazine"}:
        roles = stage_roles_for_count(stage_count)
        adjusted_departure_weights: list[float] = []
        for index, propellant in enumerate(propellants[:-1]):
            role, _ = roles[index]
            weight = blended_departure[index]
            if propellant == "lox_lh2":
                weight *= 1.10 if role in {"departure_upper", "departure_core"} else 1.05
            elif propellant in {"nto_mmh", "hydrazine"}:
                weight *= 0.92
            adjusted_departure_weights.append(weight)
        blended_departure = normalize_stage_shares(np.asarray(adjusted_departure_weights, dtype=np.float64))

    return (
        np.concatenate([blended_departure * departure_total_share, np.asarray([arrival_share], dtype=np.float64)]),
        "hybrid_constrained",
    )


def is_service_stage_payload(stage_payload: dict[str, object]) -> bool:
    if stage_payload.get("segment") != "Arrival":
        return False

    dv_mps = float(stage_payload.get("dv_mps", 0.0))
    dv_share = float(stage_payload.get("dv_share", 0.0))
    tank_mass_kg = float(stage_payload.get("tank_mass_kg", 0.0))
    return (
        (dv_mps <= SERVICE_STAGE_MAX_DV_MPS or dv_share <= SERVICE_STAGE_MAX_DV_SHARE)
        and tank_mass_kg <= SERVICE_STAGE_MAX_TANK_MASS_KG
    )


def annotate_stage_payloads(stage_payloads: list[dict[str, object]]) -> tuple[list[dict[str, object]], int, int, int, str]:
    annotated: list[dict[str, object]] = []
    launch_stage_count = 0
    service_stage_count = 0
    effective_stage_count = 0

    for stage_payload in stage_payloads:
        annotated_stage = dict(stage_payload)
        is_service_stage = is_service_stage_payload(annotated_stage)
        if annotated_stage.get("segment") == "Departure":
            launch_stage_count += 1
        if is_service_stage:
            service_stage_count += 1
            stage_kind = "service"
        elif annotated_stage.get("segment") == "Departure":
            effective_stage_count += 1
            stage_kind = "launch"
        else:
            effective_stage_count += 1
            stage_kind = "arrival"

        annotated_stage["is_service_stage"] = is_service_stage
        annotated_stage["stage_kind"] = stage_kind
        annotated.append(annotated_stage)

    if service_stage_count > 0:
        stage_count_summary = f"{launch_stage_count} launch + {service_stage_count} service"
    else:
        stage_count_summary = f"{effective_stage_count} mission stages"

    return annotated, launch_stage_count, effective_stage_count, service_stage_count, stage_count_summary


def build_reasoning(
    request: MissionRequest,
    architecture_key: str,
    stage_dv_source: str,
    metadata: ModelMetadata,
    tank_carry_penalty_kg: float = 0.0,
    realism_penalty_kg: float = 0.0,
    surface_launch_thrust_penalty_kg: float = 0.0,
    selected_by_engineering_margin: bool = False,
    engineering_margin_kg: float = 0.0,
    service_stage_count: int = 0,
) -> list[str]:
    stages = architecture_key.split("__")
    reasoning: list[str] = []
    if is_surface_launch_request(request):
        ascent_dv = max(0.0, request.launch_ascent_dv_mps or 0.0)
        if ascent_dv > 0.0:
            reasoning.append(f"Mission starts from the surface, so about {ascent_dv:,.0f} m/s of ascent delta-v is folded into the departure stack.")
        else:
            reasoning.append("Mission starts from the surface, so the ranker favors at least two departure stages instead of an orbital-start stack.")
        departure_surface_gravity = resolve_departure_surface_gravity_mps2(request)
        if departure_surface_gravity < (0.7 * STANDARD_GRAVITY):
            reasoning.append(
                f"{request.origin or 'The departure world'} has low surface gravity "
                f"({departure_surface_gravity:0.2f} m/s^2), so upper-stage-class engines remain viable on liftoff."
            )
    if stages and stages[-1] in {"nto_mmh", "hydrazine"} and request.travel_days >= 120:
        reasoning.append("Long transfer time pushes the arrival stage toward storable propellants with low boiloff.")
    if "lox_lh2" in stages[:-1]:
        reasoning.append("LH2 remains attractive on departure stages because short storage time preserves its high Isp advantage.")
    if realism_penalty_kg >= 1_500.0:
        if stages[:-1].count("lox_lh2") >= 2:
            reasoning.append("A realism prior penalizes repeated LH2 departure stages because low-density tanks and extra separations snowball quickly.")
        if len(stages[:-1]) >= 4:
            reasoning.append("Four departure stages are treated as a last resort, because real hardware complexity grows faster than idealized delta-v math.")
        if request.travel_days >= 180 and stages[-1] in {"lox_lh2", "lox_ch4", "lox_rp1"}:
            reasoning.append("Long-coast cryogenic arrival stages get an extra realism penalty, so storables win unless the cryogenic stage buys a clear mass benefit.")
    if stages and stages[0] == "lox_rp1":
        reasoning.append("RP-1 in the booster usually means the model is trading some efficiency for denser tanks and lower structural mass.")
    if request.mean_distance_au <= 0.85 and stages and stages[-1] not in {"lox_lh2", "lox_ch4"}:
        reasoning.append("Near-solar heating makes long-duration cryogenic storage less attractive, so the top stage stays storable.")
    seen_origin = request.origin in metadata.origin_vocab if request.origin else False
    seen_destination = request.destination in metadata.destination_vocab if request.destination else False
    if request.origin and request.destination and seen_origin and seen_destination:
        reasoning.append(f"Mission context {request.origin} -> {request.destination} is included in the ranking from seen training bodies.")
    elif request.origin and request.destination and (not seen_origin or not seen_destination):
        unseen_parts = []
        if not seen_origin:
            unseen_parts.append(f"origin {request.origin}")
        if not seen_destination:
            unseen_parts.append(f"destination {request.destination}")
        reasoning.append(
            "This mission is partly out-of-distribution for the ranker because "
            + ", ".join(unseen_parts)
            + " was not present in the training vocabulary."
        )
    if stage_dv_source == "hybrid_constrained":
        reasoning.append("Stage delta-v is passed through a propellant-aware allocator so long-coast cryogenic stages keep only a small correction reserve.")
    elif stage_dv_source != "ml_head":
        reasoning.append("This checkpoint predates stage-delta-v supervision, so the per-stage split comes from the built-in physics heuristic.")
    if metadata.feature_version >= 6:
        reasoning.append("This checkpoint was trained with explicit dry-mass and tank-mass auxiliary targets, so inert-mass tradeoffs are baked into the ranker itself.")
    if service_stage_count > 0:
        reasoning.append("A tiny arrival delta-v tail is treated as a service or correction module rather than a full extra launch stage.")
    if selected_by_engineering_margin:
        reasoning.append(
            f"Within an engineering margin of about {engineering_margin_kg:,.0f} kg-eq, "
            "the reranker prefers the simpler stage count over a marginal mass gain."
        )
    if tank_carry_penalty_kg >= 50.0:
        reasoning.append("A tank-carry prior penalizes extra dry tank mass that lower stages must lift until separation, so over-staging needs a clear payoff.")
    if surface_launch_thrust_penalty_kg >= 500.0:
        reasoning.append("Surface-launch candidates get penalized when the smallest available engine cluster would create an excessive liftoff TWR, so Mars departures avoid Earth-class brute force where possible.")
    if not reasoning:
        reasoning.append("The chosen architecture is the lowest predicted mass-equivalent design among the trained candidates.")
    return reasoning


def load_model(model_dir: Path, device: torch.device) -> tuple[ArchitectureRanker, ModelMetadata, bool]:
    metadata = ModelMetadata.load(model_dir / "metadata.json")
    checkpoint = torch.load(model_dir / "model.pt", map_location=device)
    model = ArchitectureRanker(**checkpoint["model_config"]).to(device)
    load_result = model.load_state_dict(checkpoint["model_state_dict"], strict=False)
    unexpected_keys = list(load_result.unexpected_keys)
    missing_keys = list(load_result.missing_keys)
    
    # Allow unexpected keys from backbone (legacy architecture weights)
    # These are harmless and just indicate the checkpoint was saved with a different model definition
    backbone_unexpected = [k for k in unexpected_keys if k.startswith("backbone.")]
    critical_unexpected = [k for k in unexpected_keys if not k.startswith("backbone.")]
    if critical_unexpected:
        raise RuntimeError(f"Checkpoint contains unexpected critical keys: {critical_unexpected}")

    supported_missing = {
        "stage_allocation_head.weight",
        "stage_allocation_head.bias",
        "stage_tank_share_head.weight",
        "stage_tank_share_head.bias",
        "aux_output_head.weight",
        "aux_output_head.bias",
    }
    
    # Also allow backbone weights to be missing (it means current model is simpler than saved model)
    backbone_missing = [k for k in missing_keys if k.startswith("backbone.")]
    other_missing = [k for k in missing_keys if not k.startswith("backbone.")]
    
    unsupported_missing = [key for key in other_missing if key not in supported_missing]
    if unsupported_missing:
        raise RuntimeError(f"Checkpoint is missing required weights: {unsupported_missing}")

    model.eval()
    supports_stage_dv_head = not other_missing
    return model, metadata, supports_stage_dv_head


@torch.no_grad()
def score_architectures(
    model: ArchitectureRanker,
    metadata: ModelMetadata,
    request: MissionRequest,
    device: torch.device,
    batch_size: int,
    supports_stage_dv_head: bool,
) -> list[dict[str, object]]:
    transformed = transform_request(request)
    numeric_values = np.asarray(
        [transformed.get(column, metadata.numeric_feature_defaults[column]) for column in NUMERIC_FEATURE_COLUMNS],
        dtype=np.float32,
    )
    means = np.asarray(metadata.numeric_means, dtype=np.float32)
    stds = np.asarray(metadata.numeric_stds, dtype=np.float32)
    numeric_values = (numeric_values - means) / stds

    propellant_to_id = {key: index for index, key in enumerate(metadata.propellant_vocab)}
    engine_to_id = {key: index for index, key in enumerate(metadata.engine_vocab)}
    architecture_keys = metadata.architecture_keys
    stage_counts = np.asarray([len(key.split("__")) for key in architecture_keys], dtype=np.int64)
    stage_propellant_ids = np.asarray(
        [[propellant_to_id[propellant] for propellant in parse_architecture_key(key)] for key in architecture_keys],
        dtype=np.int64,
    )
    stage_engine_ids = np.asarray(
        [
            [
                engine_to_id.get(engine_key, 0)
                for engine_key in (
                    stage_engines_for_architecture(key.split("__"), len(key.split("__")))
                    + [ENGINE_PAD_TOKEN] * (metadata.max_stage_count - len(key.split("__")))
                )
            ]
            for key in architecture_keys
        ],
        dtype=np.int64,
    )
    if metadata.stage_numeric_feature_columns:
        stage_numeric_values = np.asarray(
            [
                stage_numeric_features_for_architecture(
                    request,
                    key.split("__"),
                    stage_engine_specs_for_architecture(key.split("__"), len(key.split("__"))),
                    metadata.max_stage_count,
                )
                for key in architecture_keys
            ],
            dtype=np.float32,
        )
        stage_numeric_means = np.asarray(metadata.stage_numeric_means, dtype=np.float32)
        stage_numeric_stds = np.asarray(metadata.stage_numeric_stds, dtype=np.float32)
        stage_numeric_values = (stage_numeric_values - stage_numeric_means) / stage_numeric_stds
    else:
        stage_numeric_values = np.zeros((len(architecture_keys), 0), dtype=np.float32)
    origin_id = resolve_category_id(request.origin, metadata.origin_vocab)
    destination_id = resolve_category_id(request.destination, metadata.destination_vocab)
    allocation_total_dv_mps = resolve_stage_allocation_total_dv(request)
    tank_carry_penalty_scale = resolve_tank_carry_penalty_scale(metadata)

    results: list[dict[str, object]] = []
    for start in range(0, len(architecture_keys), batch_size):
        stop = min(start + batch_size, len(architecture_keys))
        row_count = stop - start
        numeric_batch = torch.from_numpy(np.repeat(numeric_values[None, :], row_count, axis=0)).to(device)
        origin_batch = torch.full((row_count,), origin_id, dtype=torch.long, device=device)
        destination_batch = torch.full((row_count,), destination_id, dtype=torch.long, device=device)
        stage_numeric_batch = torch.from_numpy(stage_numeric_values[start:stop]).to(device)
        stage_batch = torch.from_numpy(stage_propellant_ids[start:stop]).to(device)
        engine_batch = torch.from_numpy(stage_engine_ids[start:stop]).to(device)
        count_batch = torch.from_numpy(stage_counts[start:stop]).to(device)

        outputs = model(numeric_batch, origin_batch, destination_batch, stage_batch, count_batch, engine_batch, stage_numeric_batch)
        predicted_score = np.expm1(outputs["score_log"].detach().cpu().numpy().astype(np.float64))
        predicted_launch_mass = np.expm1(outputs["launch_mass_log"].detach().cpu().numpy().astype(np.float64))
        predicted_boiloff = np.expm1(outputs["boiloff_log"].detach().cpu().numpy().astype(np.float64))
        predicted_stage_shares = (
            outputs["stage_dv_share"].detach().cpu().numpy().astype(np.float64)
            if supports_stage_dv_head
            else None
        )

        for offset, architecture_key in enumerate(architecture_keys[start:stop]):
            parts = architecture_key.split("__")
            roles = stage_roles_for_count(len(parts))
            engine_specs = stage_engine_specs_for_architecture(parts, len(parts))
            complexity_penalty_kg = compute_stage_complexity_prior_kg(request, len(parts))
            realism_penalty_kg = compute_architecture_realism_penalty_kg(request, parts)
            stage_shares, stage_dv_source = allocate_stage_dv_shares_hybrid(
                request,
                parts,
                predicted_stage_shares[offset] if predicted_stage_shares is not None else None,
            )
            mass_estimate = estimate_architecture_mass_penalty(
                request,
                parts,
                stage_shares,
                engine_specs,
                tank_carry_penalty_scale=tank_carry_penalty_scale,
            )
            operational_penalty_kg = mass_estimate.total_operational_penalty_kg
            tank_carry_penalty_kg = mass_estimate.tank_carry_penalty_kg
            surface_launch_thrust_penalty_kg = compute_surface_launch_thrust_penalty_kg(request, mass_estimate.stages)
            total_penalty_kg = complexity_penalty_kg + realism_penalty_kg + tank_carry_penalty_kg + surface_launch_thrust_penalty_kg
            estimated_total_score_kg = mass_estimate.estimated_launch_mass_kg + operational_penalty_kg + total_penalty_kg

            stage_payload = []
            for index, propellant in enumerate(parts):
                role, segment = roles[index]
                dv_share = float(stage_shares[index])
                stage_mass = mass_estimate.stages[index]
                stage_payload.append(
                    {
                        "stage_number": index + 1,
                        "role": role,
                        "segment": segment,
                        "propellant_key": propellant,
                        "propellant_label": PROPELLANT_LABELS.get(propellant, propellant),
                        "engine_key": stage_mass.engine_key,
                        "engine_label": stage_mass.engine_label,
                        "engine_count": stage_mass.engine_count,
                        "engine_vacuum_isp_seconds": stage_mass.engine_vacuum_isp_seconds,
                        "engine_vacuum_thrust_kn": stage_mass.engine_vacuum_thrust_kn,
                        "available_thrust_kn": stage_mass.available_thrust_kn,
                        "tank_mass_kg": stage_mass.tank_mass_kg,
                        "operational_penalty_kg": stage_mass.operational_penalty_kg,
                        "dv_share": dv_share,
                        "dv_mps": float(dv_share * allocation_total_dv_mps),
                    }
                )
            stage_payload, launch_stage_count, effective_stage_count, service_stage_count, stage_count_summary = annotate_stage_payloads(stage_payload)
            results.append(
                {
                    "architecture_key": architecture_key,
                    "stage_count": len(parts),
                    "launch_stage_count": int(launch_stage_count),
                    "effective_stage_count": int(effective_stage_count),
                    "service_stage_count": int(service_stage_count),
                    "stage_count_summary": stage_count_summary,
                    "predicted_raw_score_kg_equivalent": float(predicted_score[offset]),
                    "complexity_penalty_kg": float(complexity_penalty_kg),
                    "realism_penalty_kg": float(realism_penalty_kg),
                    "operational_penalty_kg": float(operational_penalty_kg),
                    "tank_carry_penalty_kg": float(tank_carry_penalty_kg),
                    "surface_launch_thrust_penalty_kg": float(surface_launch_thrust_penalty_kg),
                    "estimated_tank_carry_mass_kg": float(mass_estimate.tank_carry_mass_kg),
                    "estimated_total_tank_mass_kg": float(mass_estimate.total_tank_mass_kg),
                    "estimated_launch_mass_with_tanks_kg": float(mass_estimate.estimated_launch_mass_kg),
                    "estimated_launch_mass_engine_only_kg": float(mass_estimate.estimated_engine_only_launch_mass_kg),
                    "estimated_total_score_kg_equivalent": float(estimated_total_score_kg),
                    "predicted_score_kg_equivalent": float(predicted_score[offset] + total_penalty_kg),
                    "predicted_launch_mass_kg": float(predicted_launch_mass[offset]),
                    "predicted_boiloff_mass_kg": float(predicted_boiloff[offset]),
                    "allocation_total_dv_mps": float(allocation_total_dv_mps),
                    "stage_dv_source": stage_dv_source,
                    "stages": stage_payload,
                }
            )

    return apply_engineering_rerank(results)


def main() -> None:
    args = parse_args()
    request = load_request(args)
    device = select_device(args.device)
    model_dir = Path(args.model_dir)
    model, metadata, supports_stage_dv_head = load_model(model_dir, device)
    ranked = score_architectures(model, metadata, request, device, args.batch_size, supports_stage_dv_head)

    best = ranked[0]
    reasoning = build_reasoning(
        request,
        best["architecture_key"],
        best["stage_dv_source"],
        metadata,
        float(best.get("tank_carry_penalty_kg", 0.0)),
        float(best.get("realism_penalty_kg", 0.0)),
        float(best.get("surface_launch_thrust_penalty_kg", 0.0)),
        bool(best.get("selected_by_engineering_margin", False)),
        float(best.get("engineering_margin_kg", 0.0)),
        int(best.get("service_stage_count", 0)),
    )

    print(
        f"Best architecture: {best['architecture_key']} "
        f"({best['stage_count']} stages, predicted score {best['predicted_score_kg_equivalent']:,.0f} kg-eq)"
    )
    if best.get("stage_count_summary"):
        print(f"Stage interpretation: {best['stage_count_summary']}")
    print(f"Estimated engineering score: {best.get('estimated_total_score_kg_equivalent', 0.0):,.0f} kg-eq")
    print(f"Predicted launch mass: {best['predicted_launch_mass_kg']:,.0f} kg")
    print(f"Predicted boiloff mass: {best['predicted_boiloff_mass_kg']:,.0f} kg")
    print(
        "Applied priors: "
        f"operational {best.get('operational_penalty_kg', 0.0):,.0f} kg-eq, "
        f"stage complexity {best['complexity_penalty_kg']:,.0f} kg-eq, "
        f"architecture realism {best.get('realism_penalty_kg', 0.0):,.0f} kg-eq, "
        f"tank carry {best.get('tank_carry_penalty_kg', 0.0):,.0f} kg-eq, "
        f"surface thrust {best.get('surface_launch_thrust_penalty_kg', 0.0):,.0f} kg-eq"
    )
    print(
        "Estimated tank burden: "
        f"{best.get('estimated_tank_carry_mass_kg', 0.0):,.0f} kg carried through staging "
        f"({best.get('estimated_total_tank_mass_kg', 0.0):,.0f} kg dry tanks total)"
    )
    print(f"Allocated stage delta-v total: {best['allocation_total_dv_mps']:,.0f} m/s")
    if request.correction_reserve_dv_mps:
        print(f"Includes correction reserve: {request.correction_reserve_dv_mps:,.0f} m/s")
    print(
        "Stage delta-v source: "
        + (
            "ML head"
            if best["stage_dv_source"] == "ml_head"
            else "hybrid constrained"
            if best["stage_dv_source"] == "hybrid_constrained"
            else "physics heuristic"
        )
    )
    print("Stages:")
    for stage in best["stages"]:
        print(
            f"  {stage['stage_number']}. {stage['role']} [{stage['segment']}] -> "
            f"{stage['propellant_label']} ({stage['propellant_key']}), "
            f"engine={stage['engine_label']} ({stage['engine_key']}), "
            f"count={stage.get('engine_count', 0)}, "
            f"kind={stage.get('stage_kind', 'mission')}, "
            f"thrust={stage.get('available_thrust_kn', 0.0):,.0f} kN, "
            f"Isp={stage.get('engine_vacuum_isp_seconds', 0.0):,.1f} s, "
            f"tank={stage.get('tank_mass_kg', 0.0):,.0f} kg, "
            f"dv={stage['dv_mps']:,.0f} m/s ({stage['dv_share'] * 100.0:.1f}%)"
        )
    print("Why this design:")
    for line in reasoning:
        print(f"  - {line}")

    if args.top_k > 1:
        print("Top alternatives:")
        for index, candidate in enumerate(ranked[:args.top_k], start=1):
            print(
                f"  {index}. {candidate['architecture_key']} | "
                f"stages={candidate.get('stage_count_summary', candidate['stage_count'])} | "
                f"score={candidate['predicted_score_kg_equivalent']:,.0f} kg-eq | "
                f"engineering={candidate.get('estimated_total_score_kg_equivalent', 0.0):,.0f} kg-eq | "
                f"launch_mass={candidate['predicted_launch_mass_kg']:,.0f} kg"
            )

    if args.output_json:
        payload = {
            "request": request.__dict__,
            "best": best,
            "reasoning": reasoning,
            "top_candidates": ranked[: max(1, args.top_k)],
            "device": device.type,
            "stage_dv_head_trained": supports_stage_dv_head,
        }
        Path(args.output_json).write_text(json.dumps(payload, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
