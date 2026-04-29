from rocket_builder_ml.predict import (
    MissionRequest,
    compute_architecture_realism_penalty_kg,
    compute_stage_complexity_prior_kg,
    compute_tank_carry_penalty_kg,
)


def make_request(**overrides):
    payload = {
        "dv_total_mps": 7900.0,
        "travel_days": 280.0,
        "mean_distance_au": 3.2,
        "min_distance_au": 1.0,
        "payload_mass_kg": 1000.0,
    }
    payload.update(overrides)
    return MissionRequest(**payload)


def test_stage_complexity_penalty_grows_with_stage_count():
    request = make_request()

    assert compute_stage_complexity_prior_kg(request, 2) == 0.0
    assert compute_stage_complexity_prior_kg(request, 3) > 0.0
    assert compute_stage_complexity_prior_kg(request, 4) > compute_stage_complexity_prior_kg(request, 3)
    assert compute_stage_complexity_prior_kg(request, 5) > compute_stage_complexity_prior_kg(request, 4)


def test_stage_complexity_penalty_payload_term_caps():
    medium_payload = make_request(payload_mass_kg=20_000.0)
    huge_payload = make_request(payload_mass_kg=100_000.0)

    assert compute_stage_complexity_prior_kg(huge_payload, 4) == compute_stage_complexity_prior_kg(medium_payload, 4)


def test_surface_launch_penalizes_single_departure_stage_and_relaxes_three_stage_penalty():
    surface_launch = make_request(
        dv_total_mps=13_800.0,
        launch_ascent_dv_mps=3_500.0,
        surface_launch=True,
    )
    orbital_start = make_request(
        dv_total_mps=13_800.0,
        launch_ascent_dv_mps=0.0,
        surface_launch=False,
    )

    assert compute_stage_complexity_prior_kg(surface_launch, 2) > compute_stage_complexity_prior_kg(orbital_start, 2)
    assert compute_stage_complexity_prior_kg(surface_launch, 3) < compute_stage_complexity_prior_kg(orbital_start, 3)


def test_tank_carry_penalty_grows_for_long_cryogenic_storage():
    short_trip = make_request(travel_days=60.0, mean_distance_au=1.5)
    long_trip = make_request(travel_days=500.0, mean_distance_au=3.2)

    short_lh2 = compute_tank_carry_penalty_kg(short_trip, ["lox_rp1", "lox_lh2"], [0.75, 0.25])
    long_lh2 = compute_tank_carry_penalty_kg(long_trip, ["lox_rp1", "lox_lh2"], [0.75, 0.25])
    short_storable = compute_tank_carry_penalty_kg(short_trip, ["lox_rp1", "nto_mmh"], [0.75, 0.25])
    long_storable = compute_tank_carry_penalty_kg(long_trip, ["lox_rp1", "nto_mmh"], [0.75, 0.25])

    assert short_lh2 > 0.0
    assert long_lh2 > short_lh2
    assert (long_lh2 - short_lh2) > (long_storable - short_storable)


def test_realism_penalty_hits_hydrolox_heavy_surface_stack():
    request = make_request(
        dv_total_mps=14_000.0,
        travel_days=260.0,
        min_distance_au=1.0,
        launch_ascent_dv_mps=9_200.0,
        surface_launch=True,
    )

    mixed_penalty = compute_architecture_realism_penalty_kg(
        request,
        ["lox_ch4", "lox_ch4", "lox_lh2", "nto_mmh"],
    )
    hydrolox_heavy_penalty = compute_architecture_realism_penalty_kg(
        request,
        ["lox_lh2", "lox_lh2", "lox_lh2", "lox_lh2", "nto_mmh"],
    )

    assert hydrolox_heavy_penalty > mixed_penalty
    assert hydrolox_heavy_penalty >= 20_000.0


def test_realism_penalty_discourages_long_coast_cryogenic_arrival():
    request = make_request(
        dv_total_mps=10_500.0,
        travel_days=420.0,
        min_distance_au=0.82,
    )

    storable_arrival = compute_architecture_realism_penalty_kg(
        request,
        ["lox_ch4", "lox_lh2", "nto_mmh"],
    )
    methane_arrival = compute_architecture_realism_penalty_kg(
        request,
        ["lox_ch4", "lox_lh2", "lox_ch4"],
    )
    hydrolox_arrival = compute_architecture_realism_penalty_kg(
        request,
        ["lox_ch4", "lox_lh2", "lox_lh2"],
    )

    assert methane_arrival > storable_arrival
    assert hydrolox_arrival > methane_arrival
