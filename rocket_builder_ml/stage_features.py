from __future__ import annotations

import math
from typing import Sequence

from .engines import EngineSpec
from .propellants import get_propellant_spec

SOLAR_FLUX_AT_ONE_AU_W_M2 = 1361.0
DEFAULT_DEPARTURE_STORAGE_DAYS = 15.0

STAGE_NUMERIC_FEATURE_SUFFIXES = (
    "storage_days",
    "solar_flux_relative",
    "engine_isp_seconds",
    "engine_thrust_kn",
    "engine_dry_mass_kg",
    "propellant_density_kg_m3",
    "effective_tankage_factor",
    "boiloff_fraction",
)

_DEPARTURE_STORAGE_DAY_FRACTIONS = (
    (0.22,),
    (0.08, 0.28),
    (0.05, 0.12, 0.24),
    (0.04, 0.08, 0.14, 0.22),
)


def stage_numeric_feature_columns(max_stage_count: int) -> list[str]:
    columns: list[str] = []
    for stage_number in range(1, max_stage_count + 1):
        columns.extend(f"stage_{stage_number}_{suffix}" for suffix in STAGE_NUMERIC_FEATURE_SUFFIXES)
    return columns


def departure_storage_days_for_stage(
    total_departure_storage_days: float,
    departure_stage_count: int,
    departure_index: int,
) -> float:
    fractions = _DEPARTURE_STORAGE_DAY_FRACTIONS[max(1, departure_stage_count) - 1]
    return max(0.2, total_departure_storage_days * fractions[departure_index])


def resolve_stage_storage_days(
    travel_days: float,
    departure_storage_days: float,
    stage_count: int,
    stage_index: int,
    segment: str,
) -> float:
    if segment == "Arrival":
        return max(0.2, travel_days)
    return departure_storage_days_for_stage(departure_storage_days, max(1, stage_count - 1), stage_index)


def solar_flux_relative_from_distance(distance_au: float) -> float:
    return 1.0 / math.pow(max(0.2, distance_au), 2)


def resolve_stage_solar_flux_relative(
    departure_distance_au: float | None,
    mean_distance_au: float,
    departure_solar_flux_w_m2: float | None,
    mean_solar_flux_w_m2: float | None,
    segment: str,
) -> float:
    if segment == "Arrival":
        if mean_solar_flux_w_m2 is not None:
            return max(1.0, mean_solar_flux_w_m2) / SOLAR_FLUX_AT_ONE_AU_W_M2
        return solar_flux_relative_from_distance(mean_distance_au)

    if departure_solar_flux_w_m2 is not None:
        return max(1.0, departure_solar_flux_w_m2) / SOLAR_FLUX_AT_ONE_AU_W_M2

    reference_distance = departure_distance_au if departure_distance_au is not None else mean_distance_au
    return solar_flux_relative_from_distance(reference_distance)


def build_stage_numeric_feature_vector(
    *,
    travel_days: float,
    mean_distance_au: float,
    departure_distance_au: float | None,
    departure_solar_flux_w_m2: float | None,
    mean_solar_flux_w_m2: float | None,
    propellant_keys: Sequence[str],
    engine_specs: Sequence[EngineSpec | None],
    segments: Sequence[str],
    max_stage_count: int,
    departure_storage_days: float = DEFAULT_DEPARTURE_STORAGE_DAYS,
) -> list[float]:
    features: list[float] = []
    stage_count = len(propellant_keys)

    for slot in range(max_stage_count):
        if slot >= stage_count:
            features.extend(0.0 for _ in STAGE_NUMERIC_FEATURE_SUFFIXES)
            continue

        segment = segments[slot]
        storage_days = resolve_stage_storage_days(
            travel_days=travel_days,
            departure_storage_days=departure_storage_days,
            stage_count=stage_count,
            stage_index=slot,
            segment=segment,
        )
        solar_flux_relative = resolve_stage_solar_flux_relative(
            departure_distance_au=departure_distance_au,
            mean_distance_au=mean_distance_au,
            departure_solar_flux_w_m2=departure_solar_flux_w_m2,
            mean_solar_flux_w_m2=mean_solar_flux_w_m2,
            segment=segment,
        )

        engine = engine_specs[slot]
        propellant = get_propellant_spec(propellant_keys[slot])
        engine_isp_seconds = engine.vacuum_isp_seconds if engine is not None else 0.0
        engine_thrust_kn = engine.vacuum_thrust_kn if engine is not None else 0.0
        engine_dry_mass_kg = engine.dry_mass_kg if engine is not None else 0.0

        if propellant is None:
            propellant_density = 0.0
            effective_tankage_factor = 0.0
            boiloff_fraction = 0.0
        else:
            propellant_density = propellant.mixture_density_kg_per_m3
            effective_tankage_factor = propellant.effective_tankage_factor(storage_days)
            boiloff_fraction = propellant.compute_boiloff_fraction(storage_days, solar_flux_relative)

        features.extend(
            (
                float(storage_days),
                float(solar_flux_relative),
                float(engine_isp_seconds),
                float(engine_thrust_kn),
                float(engine_dry_mass_kg),
                float(propellant_density),
                float(effective_tankage_factor),
                float(boiloff_fraction),
            )
        )

    return features
