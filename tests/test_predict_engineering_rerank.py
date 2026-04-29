from rocket_builder_ml.predict import annotate_stage_payloads, apply_engineering_rerank


def make_candidate(
    architecture_key: str,
    stage_count: int,
    predicted_score_kg_equivalent: float,
    estimated_total_score_kg_equivalent: float,
    estimated_total_tank_mass_kg: float,
) -> dict[str, object]:
    return {
        "architecture_key": architecture_key,
        "stage_count": stage_count,
        "predicted_score_kg_equivalent": predicted_score_kg_equivalent,
        "predicted_raw_score_kg_equivalent": predicted_score_kg_equivalent,
        "estimated_total_score_kg_equivalent": estimated_total_score_kg_equivalent,
        "estimated_total_tank_mass_kg": estimated_total_tank_mass_kg,
    }


def test_engineering_rerank_prefers_fewer_stages_within_margin():
    ranked = apply_engineering_rerank(
        [
            make_candidate("three_stage", 3, 28_000.0, 16_500.0, 1_320.0),
            make_candidate("two_stage", 2, 37_500.0, 16_650.0, 1_540.0),
        ]
    )

    assert ranked[0]["architecture_key"] == "two_stage"
    assert ranked[0]["engineering_close_to_best"] is True
    assert ranked[0]["selected_by_engineering_margin"] is True


def test_engineering_rerank_keeps_best_engineering_candidate_outside_margin():
    ranked = apply_engineering_rerank(
        [
            make_candidate("three_stage", 3, 32_000.0, 16_500.0, 1_320.0),
            make_candidate("two_stage", 2, 26_000.0, 19_500.0, 1_540.0),
        ]
    )

    assert ranked[0]["architecture_key"] == "three_stage"
    assert ranked[0]["selected_by_engineering_margin"] is False


def test_small_arrival_tail_is_classified_as_service_stage():
    annotated, launch_stage_count, effective_stage_count, service_stage_count, summary = annotate_stage_payloads(
        [
            {"segment": "Departure", "dv_mps": 3870.0, "dv_share": 0.66, "tank_mass_kg": 1320.0},
            {"segment": "Departure", "dv_mps": 1930.0, "dv_share": 0.329, "tank_mass_kg": 164.0},
            {"segment": "Arrival", "dv_mps": 60.0, "dv_share": 0.01, "tank_mass_kg": 6.0},
        ]
    )

    assert launch_stage_count == 2
    assert effective_stage_count == 2
    assert service_stage_count == 1
    assert summary == "2 launch + 1 service"
    assert annotated[-1]["is_service_stage"] is True
    assert annotated[-1]["stage_kind"] == "service"
