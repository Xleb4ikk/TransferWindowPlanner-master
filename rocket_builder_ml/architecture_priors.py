from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence

import numpy as np


@dataclass(frozen=True)
class ArchitectureRealismBreakdown:
    departure_complexity_kg: float
    departure_hydrolox_kg: float
    arrival_storage_kg: float
    total_kg: float


def is_surface_launch_context(surface_launch: bool | None, launch_ascent_dv_mps: float | None) -> bool:
    return bool(surface_launch) or max(0.0, launch_ascent_dv_mps or 0.0) >= 250.0


def compute_stage_complexity_prior_kg(
    dv_total_mps: float,
    stage_count: int,
    *,
    surface_launch: bool | None = None,
    launch_ascent_dv_mps: float | None = None,
) -> float:
    if stage_count <= 2:
        if is_surface_launch_context(surface_launch, launch_ascent_dv_mps):
            ascent_dv = max(0.0, launch_ascent_dv_mps or 0.0)
            return float(np.clip(250.0 + (0.18 * ascent_dv), 300.0, 1_250.0))
        return 0.0

    if dv_total_mps < 11_000:
        stage_3_penalty = 1_200.0
    elif dv_total_mps < 13_000:
        stage_3_penalty = 600.0
    else:
        stage_3_penalty = 300.0

    if is_surface_launch_context(surface_launch, launch_ascent_dv_mps):
        stage_3_penalty *= 0.45

    penalty_kg = stage_3_penalty
    if stage_count >= 4:
        penalty_kg += 2_000.0 * (stage_count - 3)

    return float(penalty_kg)


def compute_architecture_realism_breakdown(
    propellants: Sequence[str],
    *,
    dv_total_mps: float,
    travel_days: float,
    min_distance_au: float,
    surface_launch: bool | None = None,
    launch_ascent_dv_mps: float | None = None,
) -> ArchitectureRealismBreakdown:
    if not propellants:
        return ArchitectureRealismBreakdown(0.0, 0.0, 0.0, 0.0)

    departure = list(propellants[:-1])
    arrival = propellants[-1]
    departure_stage_count = len(departure)
    surface_start = is_surface_launch_context(surface_launch, launch_ascent_dv_mps)

    # Truly extreme missions still need more staging freedom, so soften the
    # departure realism priors as mission delta-v climbs into the outer-planet range.
    departure_difficulty_scale = 1.0
    if dv_total_mps >= 18_000:
        departure_difficulty_scale = 0.65
    elif dv_total_mps >= 14_000:
        departure_difficulty_scale = 0.80

    departure_complexity_kg = 0.0
    if departure_stage_count >= 3:
        departure_complexity_kg += (1_000.0 if surface_start else 500.0) * math_pow2(departure_stage_count - 2)
        if departure_stage_count >= 4:
            departure_complexity_kg += 2_800.0 if surface_start else 2_000.0

    hydrolox_kg = 0.0
    departure_lh2_count = 0
    current_lh2_run = 0
    longest_lh2_run = 0

    for departure_index, propellant in enumerate(departure):
        role = departure_role(departure_stage_count, departure_index)
        if propellant == "lox_lh2":
            departure_lh2_count += 1
            current_lh2_run += 1
            longest_lh2_run = max(longest_lh2_run, current_lh2_run)

            if role == "booster":
                hydrolox_kg += 2_600.0 if surface_start else 1_500.0
            elif role == "departure_mid":
                hydrolox_kg += 2_200.0 if surface_start else 1_300.0
            elif role == "departure_core":
                hydrolox_kg += 1_700.0 if surface_start else 900.0
            elif role == "departure_upper" and departure_stage_count >= 3:
                hydrolox_kg += 350.0 if surface_start else 200.0
        else:
            current_lh2_run = 0

    extra_departure_lh2 = max(0, departure_lh2_count - 1)
    if extra_departure_lh2 > 0:
        hydrolox_kg += extra_departure_lh2 * (2_200.0 if surface_start else 1_200.0)
        hydrolox_kg += max(0, extra_departure_lh2 - 1) * (1_400.0 if surface_start else 700.0)

    if longest_lh2_run >= 2:
        hydrolox_kg += (longest_lh2_run - 1) * (1_800.0 if surface_start else 900.0)
        hydrolox_kg += max(0, longest_lh2_run - 2) * (2_200.0 if surface_start else 1_100.0)

    if departure_stage_count >= 4 and departure and all(propellant in {"lox_lh2", "lox_ch4", "lox_rp1"} for propellant in departure):
        hydrolox_kg += 1_800.0 if surface_start else 900.0

    arrival_storage_kg = 0.0
    if travel_days >= 180.0:
        long_coast_steps = 1 + int(travel_days >= 300.0) + int(travel_days >= 600.0)
        if arrival == "lox_lh2":
            arrival_storage_kg += long_coast_steps * 1_800.0
        elif arrival == "lox_ch4":
            arrival_storage_kg += long_coast_steps * 700.0
        elif arrival == "lox_rp1":
            arrival_storage_kg += long_coast_steps * 900.0

        if arrival in {"lox_lh2", "lox_ch4", "lox_rp1"}:
            thermal_margin = max(0.0, 0.95 - min_distance_au)
            if thermal_margin > 0.0:
                thermal_base = {
                    "lox_lh2": 2_200.0,
                    "lox_ch4": 900.0,
                    "lox_rp1": 1_200.0,
                }[arrival]
                arrival_storage_kg += thermal_base * min(1.0, thermal_margin / 0.35)

    departure_complexity_kg *= departure_difficulty_scale
    hydrolox_kg *= departure_difficulty_scale
    total_kg = departure_complexity_kg + hydrolox_kg + arrival_storage_kg
    return ArchitectureRealismBreakdown(
        departure_complexity_kg=float(departure_complexity_kg),
        departure_hydrolox_kg=float(hydrolox_kg),
        arrival_storage_kg=float(arrival_storage_kg),
        total_kg=float(total_kg),
    )


def compute_architecture_realism_penalty_kg(
    propellants: Sequence[str],
    *,
    dv_total_mps: float,
    travel_days: float,
    min_distance_au: float,
    surface_launch: bool | None = None,
    launch_ascent_dv_mps: float | None = None,
) -> float:
    return compute_architecture_realism_breakdown(
        propellants,
        dv_total_mps=dv_total_mps,
        travel_days=travel_days,
        min_distance_au=min_distance_au,
        surface_launch=surface_launch,
        launch_ascent_dv_mps=launch_ascent_dv_mps,
    ).total_kg


def departure_role(departure_stage_count: int, departure_index: int) -> str:
    if departure_stage_count <= 1:
        return "departure_core"
    if departure_index == 0:
        return "booster"
    if departure_index == departure_stage_count - 1:
        return "departure_upper"
    return "departure_mid"


def math_pow2(value: int) -> float:
    return float(value * value)
