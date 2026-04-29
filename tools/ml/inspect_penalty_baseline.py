#!/usr/bin/env python3
"""
Test script to verify the new complexity penalty function.
Tests baseline predictions with old penalty vs new penalty.
"""

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from rocket_builder_ml.predict import MissionRequest, compute_stage_complexity_prior_kg


def test_penalties():
    """Test the new penalty function with various missions."""

    print("=" * 80)
    print("Testing new complexity penalty function")
    print("=" * 80)

    test_cases = [
        # (name, dv_mps, travel_days, distance_au, payload_kg, stages)
        ("Mars 2-stage", 10_000, 259, 1.5, 2000, 2),
        ("Mars 3-stage", 10_000, 259, 1.5, 2000, 3),
        ("Mars 4-stage", 10_000, 259, 1.5, 2000, 4),
        ("Jupiter 2-stage", 12_500, 550, 3.5, 2500, 2),
        ("Jupiter 3-stage", 12_500, 550, 3.5, 2500, 3),
        ("Jupiter 4-stage", 12_500, 550, 3.5, 2500, 4),
        ("Saturn 2-stage", 14_500, 1400, 5.5, 3000, 2),
        ("Saturn 3-stage", 14_500, 1400, 5.5, 3000, 3),
        ("Saturn 4-stage", 14_500, 1400, 5.5, 3000, 4),
        ("Neptune 3-stage", 16_000, 4500, 15.0, 3000, 3),
        ("Neptune 4-stage", 16_000, 4500, 15.0, 3000, 4),
    ]

    for name, dv, travel_days, dist_au, payload, stages in test_cases:
        req = MissionRequest(
            dv_total_mps=float(dv),
            travel_days=float(travel_days),
            mean_distance_au=float(dist_au),
            min_distance_au=float(dist_au),
            payload_mass_kg=float(payload),
        )

        penalty = compute_stage_complexity_prior_kg(req, stages)

        # Calculate as % of Delta-V.
        dv_cost_kg = dv / 100.0  # Rough estimate: 1% of Delta-V
        penalty_pct = (penalty / dv_cost_kg * 100) if dv_cost_kg > 0 else 0

        print(
            f"\n{name:20s} | Delta-V={dv:6.0f} m/s | Stages={stages} | "
            f"Penalty={penalty:7.1f} kg | ~{penalty_pct:5.1f}% DV equiv"
        )

    print("\n" + "=" * 80)
    print("Expected behavior:")
    print("  Mars (DV < 11k)    : 2-stage preferred (0kg), 3-stage heavily penalized (400kg)")
    print("  Jupiter (11-13k)   : 2-stage preferred (0kg), 3-stage soft penalty (200kg)")
    print("  Saturn/Neptune(>13k): 3+ stages allowed (100kg for stage 3)")
    print("=" * 80)


if __name__ == "__main__":
    test_penalties()
