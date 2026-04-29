from dataclasses import replace

from rocket_builder_ml.engines import load_engine_catalog
from rocket_builder_ml.predict import (
    MissionRequest,
    allocate_stage_dv_shares_hybrid,
    compute_surface_launch_thrust_penalty_kg,
    estimate_architecture_mass_penalty,
    resolve_engine_count,
    stage_engine_specs_for_architecture,
)


MARS_SURFACE_GRAVITY_MPS2 = 3.727866112734456
EARTH_SURFACE_GRAVITY_MPS2 = 9.820224436579496


def make_request(**overrides) -> MissionRequest:
    payload = {
        "origin": "Mars",
        "destination": "Earth",
        "payload_mass_kg": 1300.0,
        "dv_total_mps": 5790.55524991,
        "travel_days": 142.241,
        "mean_distance_au": 1.300982,
        "min_distance_au": 0.991178,
        "dv_ejection_mps": 5790.55524991,
        "dv_injection_mps": 0.0,
        "launch_ascent_dv_mps": 3789.81189341,
        "departure_orbit_km": 200.0,
        "arrival_orbit_km": 50.0,
        "departure_distance_au": 1.61078646,
        "arrival_distance_au": 0.99117757,
        "departure_solar_flux_w_m2": 524.54431539,
        "mean_solar_flux_w_m2": 804.11014213,
        "phase_angle_deg": -295.45615439,
        "correction_reserve_dv_mps": 60.02230069,
        "surface_launch": True,
        "departure_surface_gravity_mps2": MARS_SURFACE_GRAVITY_MPS2,
    }
    payload.update(overrides)
    return MissionRequest(**payload)


def get_engine(engine_key: str):
    return next(engine for engine in load_engine_catalog() if engine.key == engine_key)


def estimate_request_penalty(request: MissionRequest, propellants: list[str]):
    shares, _ = allocate_stage_dv_shares_hybrid(request, propellants, None)
    estimate = estimate_architecture_mass_penalty(
        request,
        propellants,
        shares,
        stage_engine_specs_for_architecture(propellants, len(propellants)),
    )
    return estimate, compute_surface_launch_thrust_penalty_kg(request, estimate.stages)


def test_low_gravity_surface_launch_reduces_required_engine_count():
    mars_request = make_request()
    earth_request = replace(
        mars_request,
        origin="Earth",
        departure_surface_gravity_mps2=EARTH_SURFACE_GRAVITY_MPS2,
    )
    rl10b2 = get_engine("rl10b2")

    assert resolve_engine_count(mars_request, "booster", "Departure", 17_000.0, rl10b2) == 1
    assert resolve_engine_count(earth_request, "booster", "Departure", 17_000.0, rl10b2) == 2


def test_low_gravity_surface_launch_can_pick_gentler_departure_engine():
    request = make_request()
    estimate, thrust_penalty = estimate_request_penalty(request, ["lox_lh2", "nto_mmh", "hydrazine"])
    first_stage = estimate.stages[0]

    assert first_stage.engine_key in {"hm7b", "rl10b2", "vinci", "j2x"}
    assert first_stage.available_thrust_kn < 500.0
    assert thrust_penalty < 500.0


def test_surface_launch_thrust_penalty_discourages_oversized_methane_booster():
    request = make_request()
    hydrolox_estimate, hydrolox_penalty = estimate_request_penalty(request, ["lox_lh2", "nto_mmh", "hydrazine"])
    methane_estimate, methane_penalty = estimate_request_penalty(request, ["lox_ch4", "lox_lh2", "hydrazine"])

    assert hydrolox_estimate.stages[0].available_thrust_kn < methane_estimate.stages[0].available_thrust_kn
    assert hydrolox_penalty < 500.0
    assert methane_penalty > 10_000.0
