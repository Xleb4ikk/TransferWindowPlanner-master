#!/usr/bin/env python3
"""
Run full prediction examples to verify stage selection behavior.
"""

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from rocket_builder_ml.predict import (
    MissionRequest,
    load_model,
    score_architectures,
    select_device,
)


def run_mission_prediction(
    name: str,
    dv: float,
    travel_days: float,
    dist_au: float,
    payload_kg: float,
    top_k: int = 5,
):
    """Run prediction for a single mission and print results."""

    print(f"\n{'=' * 80}")
    print(f"Mission: {name}")
    print(
        f"dV={dv:,.0f} m/s | Travel={travel_days:.0f} days | "
        f"Distance={dist_au:.1f} AU | Payload={payload_kg:,.0f} kg"
    )
    print("=" * 80)

    request = MissionRequest(
        dv_total_mps=dv,
        travel_days=travel_days,
        mean_distance_au=dist_au,
        min_distance_au=dist_au,
        payload_mass_kg=payload_kg,
    )

    device = select_device("auto")
    model_dir = REPO_ROOT / "artifacts" / "rocket-builder-ml-v3-stage-dv-big"

    if not model_dir.exists():
        print(f"ERROR: Model directory not found: {model_dir}")
        return

    try:
        model, metadata, supports_stage_dv_head = load_model(model_dir, device)
        ranked = score_architectures(
            model,
            metadata,
            request,
            device,
            batch_size=1024,
            supports_stage_dv_head=supports_stage_dv_head,
        )

        print(f"\nTop {top_k} architectures:")
        print(f"{'Rank':<5} {'Stages':<7} {'Architecture':<30} {'Score (kg-eq)':<15} {'Complexity Penalty':<20}")
        print("-" * 85)

        for i, arch in enumerate(ranked[:top_k], 1):
            print(
                f"{i:<5} {arch['stage_count']:<7} {arch['architecture_key']:<30} "
                f"{arch['predicted_score_kg_equivalent']:>14,.0f} {arch['complexity_penalty_kg']:>19,.0f}"
            )

        best = ranked[0]
        print(f"\n[OK] SELECTED: {best['stage_count']} stages ({best['architecture_key']})")
        print(f"  Score: {best['predicted_score_kg_equivalent']:,.0f} kg-eq")
        print(f"  Complexity Penalty: {best['complexity_penalty_kg']:,.0f} kg")

    except Exception as exc:
        print(f"ERROR: {exc}")
        import traceback

        traceback.print_exc()


if __name__ == "__main__":
    print("Testing stage selection with NEW PENALTY FUNCTION")

    # Test cases: (name, dv, travel_days, distance_au, payload_kg)
    test_missions = [
        ("Mars Direct (2-stage preferred)", 10_166, 259, 1.5, 2000),
        ("Jupiter Flyby (2-3 stage)", 12_500, 550, 3.5, 2500),
        ("Saturn Probe (3-stage)", 14_500, 1400, 5.5, 3000),
    ]

    for mission_args in test_missions:
        run_mission_prediction(*mission_args, top_k=3)

    print(f"\n{'=' * 80}")
    print("SUMMARY: Check if Mars now selects 2 stages instead of 4-5!")
    print("=" * 80)
