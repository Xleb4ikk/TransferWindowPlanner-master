import unittest

from rocket_builder_ml.engines import select_engine_for_stage


class EngineCatalogSelectionTests(unittest.TestCase):
    def test_departure_upper_lh2_prefers_highest_rank_highest_isp_option(self) -> None:
        engine = select_engine_for_stage("lox_lh2", "departure_upper", "Departure")
        self.assertIsNotNone(engine)
        self.assertEqual(engine.key, "rl10b2")

    def test_arrival_lh2_prefers_long_coast_rl10_variant(self) -> None:
        engine = select_engine_for_stage("lox_lh2", "cruise_arrival", "Arrival")
        self.assertIsNotNone(engine)
        self.assertEqual(engine.key, "rl10b2")

    def test_hypergolic_departure_mid_prefers_superdraco(self) -> None:
        engine = select_engine_for_stage("nto_mmh", "departure_mid", "Departure")
        self.assertIsNotNone(engine)
        self.assertEqual(engine.key, "superdraco")


if __name__ == "__main__":
    unittest.main()
