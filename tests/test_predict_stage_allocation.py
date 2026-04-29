import unittest

from rocket_builder_ml.predict import (
    MissionRequest,
    allocate_stage_dv_shares_fallback,
    allocate_stage_dv_shares_hybrid,
    resolve_stage_allocation_total_dv,
)


class StageAllocationTests(unittest.TestCase):
    def make_request(self, **overrides) -> MissionRequest:
        payload = {
            "dv_total_mps": 16500.0,
            "travel_days": 300.0,
            "mean_distance_au": 3.0,
            "min_distance_au": 1.0,
            "payload_mass_kg": 20000.0,
            "origin": "Earth",
            "destination": "Jupiter",
        }
        payload.update(overrides)
        return MissionRequest(**payload)

    def test_long_coast_lh2_arrival_stage_keeps_only_small_reserve(self) -> None:
        request = self.make_request()
        shares, source = allocate_stage_dv_shares_hybrid(
            request,
            ["lox_lh2", "lox_lh2", "lox_lh2", "lox_lh2"],
            [0.10, 0.20, 0.25, 0.45],
        )

        arrival_dv = shares[-1] * resolve_stage_allocation_total_dv(request)

        self.assertEqual(source, "hybrid_constrained")
        self.assertLess(shares[-1], 0.02)
        self.assertLess(arrival_dv, 300.0)

    def test_explicit_arrival_split_is_respected_for_long_coast_lh2(self) -> None:
        request = self.make_request(
            dv_total_mps=16200.0,
            dv_ejection_mps=12000.0,
            dv_injection_mps=4200.0,
        )
        shares, _ = allocate_stage_dv_shares_hybrid(
            request,
            ["lox_lh2", "lox_lh2", "lox_lh2", "lox_lh2"],
            [0.10, 0.20, 0.25, 0.45],
        )

        self.assertAlmostEqual(shares[-1], 4200.0 / 16200.0, places=6)

    def test_explicit_zero_injection_keeps_only_reserve_share(self) -> None:
        request = self.make_request(
            dv_total_mps=5790.0,
            dv_ejection_mps=5790.0,
            dv_injection_mps=0.0,
            correction_reserve_dv_mps=60.0,
        )
        shares, _ = allocate_stage_dv_shares_hybrid(
            request,
            ["lox_lh2", "hydrazine"],
            [0.90, 0.10],
        )

        self.assertLess(shares[-1], 0.02)
        self.assertAlmostEqual(shares[-1], 60.0 / 5850.0, places=6)

    def test_fallback_allocator_uses_same_long_coast_lh2_cap(self) -> None:
        request = self.make_request()
        shares = allocate_stage_dv_shares_fallback(request, ["lox_lh2", "lox_lh2"])

        arrival_dv = shares[-1] * resolve_stage_allocation_total_dv(request)

        self.assertLess(shares[-1], 0.02)
        self.assertLess(arrival_dv, 300.0)


if __name__ == "__main__":
    unittest.main()
