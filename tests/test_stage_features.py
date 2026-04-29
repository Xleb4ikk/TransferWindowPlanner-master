from rocket_builder_ml.engines import select_engine_for_stage
from rocket_builder_ml.stage_features import build_stage_numeric_feature_vector


def test_stage_numeric_feature_vector_exposes_long_coast_cryo_penalty():
    engine_specs = [
        select_engine_for_stage("lox_rp1", "booster", "Departure"),
        select_engine_for_stage("lox_lh2", "cruise_arrival", "Arrival"),
    ]

    short_vector = build_stage_numeric_feature_vector(
        travel_days=60.0,
        mean_distance_au=1.2,
        departure_distance_au=1.0,
        departure_solar_flux_w_m2=None,
        mean_solar_flux_w_m2=None,
        propellant_keys=["lox_rp1", "lox_lh2"],
        engine_specs=engine_specs,
        segments=["Departure", "Arrival"],
        max_stage_count=5,
    )
    long_vector = build_stage_numeric_feature_vector(
        travel_days=500.0,
        mean_distance_au=3.0,
        departure_distance_au=1.0,
        departure_solar_flux_w_m2=None,
        mean_solar_flux_w_m2=None,
        propellant_keys=["lox_rp1", "lox_lh2"],
        engine_specs=engine_specs,
        segments=["Departure", "Arrival"],
        max_stage_count=5,
    )

    short_arrival_boiloff = short_vector[15]
    long_arrival_boiloff = long_vector[15]

    assert short_arrival_boiloff >= 0.0
    assert long_arrival_boiloff > short_arrival_boiloff


def test_stage_numeric_feature_vector_has_fixed_size():
    engine_specs = [select_engine_for_stage("lox_ch4", "departure_core", "Departure")]
    values = build_stage_numeric_feature_vector(
        travel_days=180.0,
        mean_distance_au=1.4,
        departure_distance_au=1.0,
        departure_solar_flux_w_m2=None,
        mean_solar_flux_w_m2=None,
        propellant_keys=["lox_ch4"],
        engine_specs=engine_specs,
        segments=["Departure"],
        max_stage_count=5,
    )

    assert len(values) == 40
