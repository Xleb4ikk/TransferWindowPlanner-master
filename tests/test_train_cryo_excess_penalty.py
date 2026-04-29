import unittest

import torch

from rocket_builder_ml.data import PROPELLANT_VOCAB
from rocket_builder_ml.train import compute_long_coast_cryo_excess_regularizer

PROPELLANT_TO_ID = {key: index for index, key in enumerate(PROPELLANT_VOCAB)}


class CryoExcessLossTests(unittest.TestCase):
    def make_batch(self, last_propellant: str, storage_days: float) -> dict[str, torch.Tensor]:
        return {
            "stage_dv_share": torch.tensor([[0.60, 0.40]], dtype=torch.float32),
            "actual_stage_total_dv_mps": torch.tensor([10000.0], dtype=torch.float32),
            "actual_stage_dv_mps": torch.tensor([[7000.0, 3000.0]], dtype=torch.float32),
            "stage_propellant_ids": torch.tensor(
                [[PROPELLANT_TO_ID["lox_ch4"], PROPELLANT_TO_ID[last_propellant]]],
                dtype=torch.long,
            ),
            "stage_count": torch.tensor([2], dtype=torch.long),
            "stage_storage_days": torch.tensor([[10.0, storage_days]], dtype=torch.float32),
        }

    def test_long_coast_lh2_stage_excess_is_penalized(self) -> None:
        batch = self.make_batch("lox_lh2", 320.0)
        loss, predicted_stage_dv, cryo_stage_excess_dv, cryo_stage_risk = compute_long_coast_cryo_excess_regularizer(
            {"stage_dv_share": batch["stage_dv_share"]},
            batch,
        )

        self.assertAlmostEqual(float(predicted_stage_dv[0, 1].item()), 4000.0, places=4)
        self.assertAlmostEqual(float(cryo_stage_excess_dv[0, 1].item()), 1000.0, places=4)
        self.assertGreater(float(cryo_stage_risk[0, 1].item()), 0.99)
        self.assertGreater(float(loss.item()), 0.49)

    def test_storable_stage_has_no_cryo_excess_penalty(self) -> None:
        batch = self.make_batch("nto_mmh", 320.0)
        loss, _, cryo_stage_excess_dv, cryo_stage_risk = compute_long_coast_cryo_excess_regularizer(
            {"stage_dv_share": batch["stage_dv_share"]},
            batch,
        )

        self.assertAlmostEqual(float(cryo_stage_excess_dv[0, 1].item()), 1000.0, places=4)
        self.assertEqual(float(cryo_stage_risk[0, 1].item()), 0.0)
        self.assertEqual(float(loss.item()), 0.0)

    def test_short_storage_lh2_stage_is_not_treated_as_long_coast(self) -> None:
        batch = self.make_batch("lox_lh2", 45.0)
        loss, _, _, cryo_stage_risk = compute_long_coast_cryo_excess_regularizer(
            {"stage_dv_share": batch["stage_dv_share"]},
            batch,
        )

        self.assertEqual(float(cryo_stage_risk[0, 1].item()), 0.0)
        self.assertEqual(float(loss.item()), 0.0)


if __name__ == "__main__":
    unittest.main()
