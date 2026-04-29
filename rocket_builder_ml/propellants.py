from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
import math


@dataclass(frozen=True)
class PropellantSpec:
    key: str
    display_name: str
    storage_class: str
    vacuum_isp_seconds: float
    mixture_density_kg_per_m3: float
    base_tankage_factor: float
    storage_complexity_factor: float
    base_boiloff_per_day_at_1au: float
    solar_flux_exponent: float
    primary_burn_penalty_factor: float
    long_coast_penalty_factor: float
    restart_penalty_factor: float
    handling_penalty_factor: float
    is_cryogenic: bool
    is_hypergolic: bool
    is_low_thrust: bool

    def effective_tankage_factor(self, storage_days: float) -> float:
        normalized_storage = math.sqrt(max(0.0, storage_days) / 30.0)
        return self.base_tankage_factor + (self.storage_complexity_factor * normalized_storage)

    def compute_boiloff_fraction(self, storage_days: float, solar_flux_relative: float) -> float:
        if self.base_boiloff_per_day_at_1au <= 0.0 or storage_days <= 0.0:
            return 0.0

        relative_flux = max(0.05, solar_flux_relative)
        daily_rate = self.base_boiloff_per_day_at_1au * math.pow(relative_flux, self.solar_flux_exponent)
        return 1.0 - math.exp(-daily_rate * storage_days)


@lru_cache(maxsize=1)
def load_propellant_catalog() -> dict[str, PropellantSpec]:
    catalog = {
        "lox_lh2": PropellantSpec(
            key="lox_lh2",
            display_name="LOX/LH2",
            storage_class="cryogenic",
            vacuum_isp_seconds=450.0,
            mixture_density_kg_per_m3=360.0,
            base_tankage_factor=0.16,
            storage_complexity_factor=0.035,
            base_boiloff_per_day_at_1au=0.0035,
            solar_flux_exponent=1.10,
            primary_burn_penalty_factor=0.00,
            long_coast_penalty_factor=0.18,
            restart_penalty_factor=0.01,
            handling_penalty_factor=0.02,
            is_cryogenic=True,
            is_hypergolic=False,
            is_low_thrust=False,
        ),
        "lox_ch4": PropellantSpec(
            key="lox_ch4",
            display_name="LOX/LCH4",
            storage_class="semi-cryogenic",
            vacuum_isp_seconds=360.0,
            mixture_density_kg_per_m3=830.0,
            base_tankage_factor=0.10,
            storage_complexity_factor=0.020,
            base_boiloff_per_day_at_1au=0.0010,
            solar_flux_exponent=1.00,
            primary_burn_penalty_factor=0.01,
            long_coast_penalty_factor=0.07,
            restart_penalty_factor=0.02,
            handling_penalty_factor=0.01,
            is_cryogenic=True,
            is_hypergolic=False,
            is_low_thrust=False,
        ),
        "lox_rp1": PropellantSpec(
            key="lox_rp1",
            display_name="LOX/RP-1",
            storage_class="semi-cryogenic",
            vacuum_isp_seconds=330.0,
            mixture_density_kg_per_m3=1020.0,
            base_tankage_factor=0.07,
            storage_complexity_factor=0.012,
            base_boiloff_per_day_at_1au=0.00035,
            solar_flux_exponent=0.95,
            primary_burn_penalty_factor=0.00,
            long_coast_penalty_factor=0.12,
            restart_penalty_factor=0.06,
            handling_penalty_factor=0.01,
            is_cryogenic=True,
            is_hypergolic=False,
            is_low_thrust=False,
        ),
        "nto_mmh": PropellantSpec(
            key="nto_mmh",
            display_name="NTO/MMH",
            storage_class="storable",
            vacuum_isp_seconds=322.0,
            mixture_density_kg_per_m3=1150.0,
            base_tankage_factor=0.09,
            storage_complexity_factor=0.004,
            base_boiloff_per_day_at_1au=0.00002,
            solar_flux_exponent=0.15,
            primary_burn_penalty_factor=0.04,
            long_coast_penalty_factor=0.00,
            restart_penalty_factor=0.00,
            handling_penalty_factor=0.03,
            is_cryogenic=False,
            is_hypergolic=True,
            is_low_thrust=False,
        ),
        "hydrazine": PropellantSpec(
            key="hydrazine",
            display_name="Hydrazine",
            storage_class="storable",
            vacuum_isp_seconds=230.0,
            mixture_density_kg_per_m3=1010.0,
            base_tankage_factor=0.12,
            storage_complexity_factor=0.005,
            base_boiloff_per_day_at_1au=0.00001,
            solar_flux_exponent=0.10,
            primary_burn_penalty_factor=0.20,
            long_coast_penalty_factor=0.01,
            restart_penalty_factor=0.00,
            handling_penalty_factor=0.04,
            is_cryogenic=False,
            is_hypergolic=False,
            is_low_thrust=True,
        ),
    }
    return dict(catalog)


def get_propellant_spec(propellant_key: str) -> PropellantSpec | None:
    return load_propellant_catalog().get(propellant_key)
