from __future__ import annotations

from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any
import json

import numpy as np
import pandas as pd
import torch
from torch.utils.data import Dataset

from .architecture_priors import compute_architecture_realism_penalty_kg
from .engines import load_engine_catalog
from .propellants import load_propellant_catalog
from .stage_features import SOLAR_FLUX_AT_ONE_AU_W_M2, stage_numeric_feature_columns

MAX_STAGE_COUNT = 5
UNKNOWN_TOKEN = "unknown"
PROPELLANT_VOCAB = ["none", "lox_lh2", "lox_ch4", "lox_rp1", "nto_mmh", "hydrazine"]
ENGINE_PAD_TOKEN = "none"

MISSION_NUMERIC_SOURCE_COLUMNS = [
    "dv_total_mps",
    "dv_ejection_mps",
    "dv_injection_mps",
    "travel_days",
    "payload_mass_kg",
    "departure_orbit_km",
    "arrival_orbit_km",
    "departure_distance_au",
    "arrival_distance_au",
    "mean_distance_au",
    "min_distance_au",
    "departure_solar_flux_w_m2",
    "mean_solar_flux_w_m2",
    "phase_angle_deg",
    "transfer_angle_deg",
    "correction_reserve_dv_mps",
]

NUMERIC_FEATURE_COLUMNS = [
    "log_dv_total_mps",
    "log_dv_ejection_mps",
    "log_dv_injection_mps",
    "ejection_dv_fraction",
    "injection_dv_fraction",
    "log_travel_days",
    "log_payload_mass_kg",
    "log_departure_orbit_km",
    "log_arrival_orbit_km",
    "departure_distance_au",
    "arrival_distance_au",
    "mean_distance_au",
    "min_distance_au",
    "departure_solar_flux_relative",
    "mean_solar_flux_relative",
    "min_solar_flux_relative",
    "phase_angle_sin",
    "phase_angle_cos",
    "transfer_angle_sin",
    "transfer_angle_cos",
    "log_correction_reserve_dv_mps",
    "long_way_flag",
]


@dataclass
class SplitFrames:
    train: pd.DataFrame
    val: pd.DataFrame
    test: pd.DataFrame


@dataclass
class ModelMetadata:
    numeric_feature_columns: list[str]
    numeric_means: list[float]
    numeric_stds: list[float]
    numeric_feature_defaults: dict[str, float]
    stage_numeric_feature_columns: list[str]
    stage_numeric_means: list[float]
    stage_numeric_stds: list[float]
    stage_numeric_feature_defaults: dict[str, float]
    propellant_vocab: list[str]
    engine_vocab: list[str]
    architecture_keys: list[str]
    origin_vocab: list[str]
    destination_vocab: list[str]
    max_stage_count: int
    feature_version: int = 4

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    def save(self, path: str | Path) -> None:
        Path(path).write_text(json.dumps(self.to_dict(), indent=2), encoding="utf-8")

    @classmethod
    def load(cls, path: str | Path) -> "ModelMetadata":
        payload = json.loads(Path(path).read_text(encoding="utf-8"))
        payload.setdefault("numeric_feature_defaults", dict(zip(payload["numeric_feature_columns"], payload["numeric_means"])))
        payload.setdefault("stage_numeric_feature_columns", [])
        payload.setdefault("stage_numeric_means", [])
        payload.setdefault("stage_numeric_stds", [])
        payload.setdefault("stage_numeric_feature_defaults", dict(zip(payload["stage_numeric_feature_columns"], payload["stage_numeric_means"])))
        payload.setdefault("origin_vocab", [UNKNOWN_TOKEN])
        payload.setdefault("destination_vocab", [UNKNOWN_TOKEN])
        payload.setdefault("engine_vocab", [ENGINE_PAD_TOKEN])
        payload.setdefault("feature_version", 1)
        return cls(**payload)


def parse_architecture_key(architecture_key: str) -> list[str]:
    parts = architecture_key.split("__")
    if len(parts) > MAX_STAGE_COUNT:
        raise ValueError(f"Architecture '{architecture_key}' has {len(parts)} stages, expected <= {MAX_STAGE_COUNT}.")
    return parts + ["none"] * (MAX_STAGE_COUNT - len(parts))


def compute_training_realism_penalty_kg(frame: pd.DataFrame) -> np.ndarray:
    penalties = [
        compute_architecture_realism_penalty_kg(
            architecture_key.split("__"),
            dv_total_mps=float(dv_total_mps),
            travel_days=float(travel_days),
            min_distance_au=float(min_distance_au),
        )
        for architecture_key, dv_total_mps, travel_days, min_distance_au in zip(
            frame["architecture_key"].astype(str),
            frame["dv_total_mps"].to_numpy(dtype=np.float64, copy=False),
            frame["travel_days"].to_numpy(dtype=np.float64, copy=False),
            frame["min_distance_au"].to_numpy(dtype=np.float64, copy=False),
        )
    ]
    return np.asarray(penalties, dtype=np.float64)


def normalize_category_series(series: pd.Series) -> pd.Series:
    return series.fillna(UNKNOWN_TOKEN).astype(str)


def build_stage_numeric_feature_frame(frame: pd.DataFrame) -> pd.DataFrame:
    enriched = frame.copy()
    propellant_catalog = load_propellant_catalog()
    engine_catalog = {engine.key: engine for engine in load_engine_catalog()}
    columns = stage_numeric_feature_columns(MAX_STAGE_COUNT)

    departure_flux_relative = np.clip(
        enriched["departure_solar_flux_w_m2"].to_numpy(dtype=np.float64) / SOLAR_FLUX_AT_ONE_AU_W_M2,
        0.0,
        None,
    )
    mean_flux_relative = np.clip(
        enriched["mean_solar_flux_w_m2"].to_numpy(dtype=np.float64) / SOLAR_FLUX_AT_ONE_AU_W_M2,
        0.0,
        None,
    )

    for stage_index in range(MAX_STAGE_COUNT):
        stage_number = stage_index + 1
        propellant_key = normalize_category_series(enriched[f"stage_{stage_number}_propellant_key"])
        engine_key = normalize_category_series(enriched[f"stage_{stage_number}_engine_key"])
        storage_days = enriched[f"stage_{stage_number}_storage_days"].fillna(0.0).to_numpy(dtype=np.float64, copy=True)
        segment = normalize_category_series(enriched.get(f"stage_{stage_number}_segment", pd.Series(["Departure"] * len(enriched), index=enriched.index)))
        active_mask = (propellant_key.ne("none") & propellant_key.ne(UNKNOWN_TOKEN)).to_numpy(dtype=bool, copy=True)
        solar_flux_relative = np.where(segment.eq("Arrival"), mean_flux_relative, departure_flux_relative)

        base_tankage_factor = propellant_key.map(lambda key: propellant_catalog.get(key).base_tankage_factor if key in propellant_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)
        storage_complexity_factor = propellant_key.map(lambda key: propellant_catalog.get(key).storage_complexity_factor if key in propellant_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)
        propellant_density = propellant_key.map(lambda key: propellant_catalog.get(key).mixture_density_kg_per_m3 if key in propellant_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)
        daily_boiloff = propellant_key.map(lambda key: propellant_catalog.get(key).base_boiloff_per_day_at_1au if key in propellant_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)
        solar_exponent = propellant_key.map(lambda key: propellant_catalog.get(key).solar_flux_exponent if key in propellant_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)

        effective_tankage_factor = base_tankage_factor + (storage_complexity_factor * np.sqrt(np.maximum(0.0, storage_days) / 30.0))
        boiloff_rate = daily_boiloff * np.power(np.maximum(0.05, solar_flux_relative), solar_exponent)
        boiloff_fraction = np.where(
            (daily_boiloff > 0.0) & (storage_days > 0.0),
            1.0 - np.exp(-(boiloff_rate * storage_days)),
            0.0,
        )

        engine_isp_seconds = engine_key.map(lambda key: engine_catalog.get(key).vacuum_isp_seconds if key in engine_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)
        engine_thrust_kn = engine_key.map(lambda key: engine_catalog.get(key).vacuum_thrust_kn if key in engine_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)
        engine_dry_mass_kg = engine_key.map(lambda key: engine_catalog.get(key).dry_mass_kg if key in engine_catalog else 0.0).to_numpy(dtype=np.float64, copy=True)

        storage_days = np.where(active_mask, storage_days, 0.0)
        solar_flux_relative = np.where(active_mask, solar_flux_relative, 0.0)
        engine_isp_seconds = np.where(active_mask, engine_isp_seconds, 0.0)
        engine_thrust_kn = np.where(active_mask, engine_thrust_kn, 0.0)
        engine_dry_mass_kg = np.where(active_mask, engine_dry_mass_kg, 0.0)
        propellant_density = np.where(active_mask, propellant_density, 0.0)
        effective_tankage_factor = np.where(active_mask, effective_tankage_factor, 0.0)
        boiloff_fraction = np.where(active_mask, boiloff_fraction, 0.0)

        prefix = f"stage_{stage_number}"
        enriched[f"{prefix}_storage_days"] = storage_days
        enriched[f"{prefix}_solar_flux_relative"] = solar_flux_relative
        enriched[f"{prefix}_engine_isp_seconds"] = engine_isp_seconds
        enriched[f"{prefix}_engine_thrust_kn"] = engine_thrust_kn
        enriched[f"{prefix}_engine_dry_mass_kg"] = engine_dry_mass_kg
        enriched[f"{prefix}_propellant_density_kg_m3"] = propellant_density
        enriched[f"{prefix}_effective_tankage_factor"] = effective_tankage_factor
        enriched[f"{prefix}_boiloff_fraction"] = boiloff_fraction

    return enriched[columns]


def add_derived_features(frame: pd.DataFrame) -> pd.DataFrame:
    enriched = frame.copy()

    total_dv = np.clip(enriched["dv_total_mps"].to_numpy(dtype=np.float64), 1.0, None)
    ejection_dv = np.clip(enriched["dv_ejection_mps"].to_numpy(dtype=np.float64), 0.0, None)
    injection_dv = np.clip(enriched["dv_injection_mps"].to_numpy(dtype=np.float64), 0.0, None)
    travel_days = np.clip(enriched["travel_days"].to_numpy(dtype=np.float64), 0.0, None)
    payload = np.clip(enriched["payload_mass_kg"].to_numpy(dtype=np.float64), 0.0, None)
    departure_orbit = np.clip(enriched["departure_orbit_km"].to_numpy(dtype=np.float64), 1.0, None)
    arrival_orbit = np.clip(enriched["arrival_orbit_km"].to_numpy(dtype=np.float64), 1.0, None)
    departure_distance = np.clip(enriched["departure_distance_au"].to_numpy(dtype=np.float64), 0.2, None)
    arrival_distance = np.clip(enriched["arrival_distance_au"].to_numpy(dtype=np.float64), 0.2, None)
    mean_distance = np.clip(enriched["mean_distance_au"].to_numpy(dtype=np.float64), 0.2, None)
    min_distance = np.clip(enriched["min_distance_au"].to_numpy(dtype=np.float64), 0.2, None)
    departure_flux = np.clip(enriched["departure_solar_flux_w_m2"].to_numpy(dtype=np.float64), 1.0, None)
    mean_flux = np.clip(enriched["mean_solar_flux_w_m2"].to_numpy(dtype=np.float64), 1.0, None)
    correction_reserve = np.clip(enriched["correction_reserve_dv_mps"].to_numpy(dtype=np.float64), 0.0, None)
    phase_rad = np.deg2rad(enriched["phase_angle_deg"].to_numpy(dtype=np.float64))
    transfer_rad = np.deg2rad(enriched["transfer_angle_deg"].to_numpy(dtype=np.float64))

    enriched["log_dv_total_mps"] = np.log1p(total_dv)
    enriched["log_dv_ejection_mps"] = np.log1p(ejection_dv)
    enriched["log_dv_injection_mps"] = np.log1p(injection_dv)
    enriched["ejection_dv_fraction"] = ejection_dv / total_dv
    enriched["injection_dv_fraction"] = injection_dv / total_dv
    enriched["log_travel_days"] = np.log1p(travel_days)
    enriched["log_payload_mass_kg"] = np.log1p(payload)
    enriched["log_departure_orbit_km"] = np.log1p(departure_orbit)
    enriched["log_arrival_orbit_km"] = np.log1p(arrival_orbit)
    enriched["departure_solar_flux_relative"] = departure_flux / 1361.0
    enriched["mean_solar_flux_relative"] = mean_flux / 1361.0
    enriched["min_solar_flux_relative"] = 1.0 / np.square(min_distance)
    enriched["phase_angle_sin"] = np.sin(phase_rad)
    enriched["phase_angle_cos"] = np.cos(phase_rad)
    enriched["transfer_angle_sin"] = np.sin(transfer_rad)
    enriched["transfer_angle_cos"] = np.cos(transfer_rad)
    enriched["log_correction_reserve_dv_mps"] = np.log1p(correction_reserve)
    enriched["long_way_flag"] = enriched["long_way"].astype(bool).astype(np.float64)
    enriched["origin"] = normalize_category_series(enriched["origin"])
    enriched["destination"] = normalize_category_series(enriched["destination"])

    stage_matrix = enriched["architecture_key"].map(parse_architecture_key).tolist()
    for stage_index in range(MAX_STAGE_COUNT):
        enriched[f"stage_{stage_index + 1}_propellant_key"] = [parts[stage_index] for parts in stage_matrix]
        engine_column = f"stage_{stage_index + 1}_engine_key"
        if engine_column not in enriched.columns:
            enriched[engine_column] = ENGINE_PAD_TOKEN
        enriched[engine_column] = enriched[engine_column].fillna(ENGINE_PAD_TOKEN).astype(str)

    stage_numeric = build_stage_numeric_feature_frame(enriched)
    for column in stage_numeric.columns:
        enriched[column] = stage_numeric[column]

    return enriched


def load_architecture_training_frame(dataset_dir: str | Path) -> pd.DataFrame:
    dataset_dir = Path(dataset_dir)
    mission_path = dataset_dir / "mission_dataset.csv"
    architecture_path = dataset_dir / "architecture_dataset.csv"

    if not mission_path.exists():
        raise FileNotFoundError(f"Mission dataset not found: {mission_path}")
    if not architecture_path.exists():
        raise FileNotFoundError(f"Architecture dataset not found: {architecture_path}")

    mission_columns = [
        "mission_id",
        "origin",
        "destination",
        "travel_days",
        "payload_mass_kg",
        "departure_orbit_km",
        "arrival_orbit_km",
        "departure_distance_au",
        "arrival_distance_au",
        "mean_distance_au",
        "min_distance_au",
        "departure_solar_flux_w_m2",
        "mean_solar_flux_w_m2",
        "dv_ejection_mps",
        "dv_injection_mps",
        "dv_total_mps",
        "phase_angle_deg",
        "transfer_angle_deg",
        "long_way",
        "correction_reserve_dv_mps",
    ]
    architecture_required_columns = [
        "mission_id",
        "architecture_key",
        "stage_count",
        "output_rank",
        "score_kg_equivalent",
        "launch_mass_kg",
        "total_boiloff_mass_kg",
        "dv_arrival_plus_reserve_mps",
    ]
    architecture_optional_columns = [
        "total_tank_mass_kg",
        "total_structure_mass_kg",
        "total_engine_mass_kg",
        "tank_carry_proxy_kg",
        "total_dry_mass_kg",
        "architecture_realism_penalty_kg",
        "total_penalty_kg",
    ]
    architecture_optional_columns.extend(f"stage_{stage_number}_dv_mps" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_storage_days" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_engine_key" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_segment" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_engine_isp_seconds" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_engine_thrust_kn" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_total_engine_mass_kg" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_tank_mass_kg" for stage_number in range(1, MAX_STAGE_COUNT + 1))
    architecture_optional_columns.extend(f"stage_{stage_number}_structure_mass_kg" for stage_number in range(1, MAX_STAGE_COUNT + 1))

    mission_frame = pd.read_csv(mission_path, usecols=mission_columns)
    architecture_header = pd.read_csv(architecture_path, nrows=0)
    available_architecture_columns = set(architecture_header.columns.tolist())
    missing_required = [column for column in architecture_required_columns if column not in available_architecture_columns]
    if missing_required:
        raise ValueError(f"Architecture dataset is missing required columns: {', '.join(missing_required)}")
    architecture_usecols = architecture_required_columns + [
        column for column in architecture_optional_columns if column in available_architecture_columns
    ]
    architecture_frame = pd.read_csv(architecture_path, usecols=architecture_usecols)
    merged = architecture_frame.merge(mission_frame, on="mission_id", how="left", validate="many_to_one")
    stage_dv_columns = [f"stage_{stage_number}_dv_mps" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_storage_columns = [f"stage_{stage_number}_storage_days" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_engine_columns = [f"stage_{stage_number}_engine_key" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_segment_columns = [f"stage_{stage_number}_segment" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_engine_isp_columns = [f"stage_{stage_number}_engine_isp_seconds" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_engine_thrust_columns = [f"stage_{stage_number}_engine_thrust_kn" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_total_engine_mass_columns = [f"stage_{stage_number}_total_engine_mass_kg" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_tank_mass_columns = [f"stage_{stage_number}_tank_mass_kg" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    stage_structure_mass_columns = [f"stage_{stage_number}_structure_mass_kg" for stage_number in range(1, MAX_STAGE_COUNT + 1)]
    for column in stage_dv_columns:
        if column not in merged.columns:
            merged[column] = 0.0
    for column in stage_storage_columns:
        if column not in merged.columns:
            merged[column] = 0.0
    for column in stage_engine_columns:
        if column not in merged.columns:
            merged[column] = ENGINE_PAD_TOKEN
    for column in stage_segment_columns:
        if column not in merged.columns:
            stage_number = int(column.split("_")[1])
            merged[column] = np.where(
                stage_number < merged["stage_count"].to_numpy(dtype=np.int64, copy=False),
                "Departure",
                "Arrival",
            )
    for column in stage_engine_isp_columns + stage_engine_thrust_columns + stage_total_engine_mass_columns + stage_tank_mass_columns + stage_structure_mass_columns:
        if column not in merged.columns:
            merged[column] = 0.0
    merged[stage_dv_columns] = merged[stage_dv_columns].fillna(0.0)
    merged[stage_storage_columns] = merged[stage_storage_columns].fillna(0.0)
    merged[stage_engine_columns] = merged[stage_engine_columns].fillna(ENGINE_PAD_TOKEN).astype(str)
    merged[stage_segment_columns] = merged[stage_segment_columns].fillna("Arrival").astype(str)
    merged[stage_engine_isp_columns] = merged[stage_engine_isp_columns].fillna(0.0)
    merged[stage_engine_thrust_columns] = merged[stage_engine_thrust_columns].fillna(0.0)
    merged[stage_total_engine_mass_columns] = merged[stage_total_engine_mass_columns].fillna(0.0)
    merged[stage_tank_mass_columns] = merged[stage_tank_mass_columns].fillna(0.0)
    merged[stage_structure_mass_columns] = merged[stage_structure_mass_columns].fillna(0.0)

    if "total_tank_mass_kg" not in merged.columns:
        merged["total_tank_mass_kg"] = merged[stage_tank_mass_columns].sum(axis=1)
    else:
        merged["total_tank_mass_kg"] = merged["total_tank_mass_kg"].fillna(merged[stage_tank_mass_columns].sum(axis=1))

    if "total_structure_mass_kg" not in merged.columns:
        merged["total_structure_mass_kg"] = merged[stage_structure_mass_columns].sum(axis=1)
    else:
        merged["total_structure_mass_kg"] = merged["total_structure_mass_kg"].fillna(merged[stage_structure_mass_columns].sum(axis=1))

    if "total_engine_mass_kg" not in merged.columns:
        merged["total_engine_mass_kg"] = merged[stage_total_engine_mass_columns].sum(axis=1)
    else:
        merged["total_engine_mass_kg"] = merged["total_engine_mass_kg"].fillna(merged[stage_total_engine_mass_columns].sum(axis=1))

    tank_carry_proxy = sum(
        merged[f"stage_{stage_number}_tank_mass_kg"] * stage_number
        for stage_number in range(1, MAX_STAGE_COUNT + 1)
    )
    if "tank_carry_proxy_kg" not in merged.columns:
        merged["tank_carry_proxy_kg"] = tank_carry_proxy
    else:
        merged["tank_carry_proxy_kg"] = merged["tank_carry_proxy_kg"].fillna(tank_carry_proxy)

    if merged.isna().any().any():
        broken_columns = [column for column, count in merged.isna().sum().items() if count]
        raise ValueError(f"Joined dataset contains missing values in: {', '.join(broken_columns)}")

    merged["architecture_key"] = merged["architecture_key"].astype(str)
    merged = add_derived_features(merged)
    if "architecture_realism_penalty_kg" not in merged.columns:
        merged["architecture_realism_penalty_kg"] = compute_training_realism_penalty_kg(merged)
        merged["training_score_kg_equivalent"] = merged["score_kg_equivalent"] + merged["architecture_realism_penalty_kg"]
    else:
        merged["architecture_realism_penalty_kg"] = merged["architecture_realism_penalty_kg"].fillna(0.0)
        merged["training_score_kg_equivalent"] = merged["score_kg_equivalent"]
    return merged.sort_values(["mission_id", "output_rank", "architecture_key"]).reset_index(drop=True)


def split_by_mission(
    frame: pd.DataFrame,
    seed: int,
    train_fraction: float = 0.80,
    val_fraction: float = 0.10,
) -> SplitFrames:
    if train_fraction <= 0 or val_fraction <= 0 or train_fraction + val_fraction >= 1:
        raise ValueError("train_fraction and val_fraction must be positive and leave room for a test split.")

    mission_ids = frame["mission_id"].drop_duplicates().to_numpy(dtype=np.int64).copy()
    if mission_ids.size < 3:
        raise ValueError("At least three unique missions are required.")

    rng = np.random.default_rng(seed)
    rng.shuffle(mission_ids)

    train_count = max(1, int(round(mission_ids.size * train_fraction)))
    val_count = max(1, int(round(mission_ids.size * val_fraction)))
    test_count = mission_ids.size - train_count - val_count
    if test_count <= 0:
        test_count = 1
        if train_count >= val_count:
            train_count -= 1
        else:
            val_count -= 1

    train_ids = set(mission_ids[:train_count].tolist())
    val_ids = set(mission_ids[train_count:train_count + val_count].tolist())
    test_ids = set(mission_ids[train_count + val_count:].tolist())

    return SplitFrames(
        train=frame[frame["mission_id"].isin(train_ids)].reset_index(drop=True),
        val=frame[frame["mission_id"].isin(val_ids)].reset_index(drop=True),
        test=frame[frame["mission_id"].isin(test_ids)].reset_index(drop=True),
    )


def build_category_vocab(series: pd.Series) -> list[str]:
    unique_values = sorted(normalize_category_series(series).drop_duplicates().tolist())
    if UNKNOWN_TOKEN in unique_values:
        unique_values = [value for value in unique_values if value != UNKNOWN_TOKEN]
    return [UNKNOWN_TOKEN] + unique_values


def build_model_metadata(train_frame: pd.DataFrame, full_frame: pd.DataFrame) -> ModelMetadata:
    numeric_stats = train_frame[NUMERIC_FEATURE_COLUMNS].astype(np.float64)
    numeric_means = numeric_stats.mean(axis=0)
    numeric_stds = numeric_stats.std(axis=0, ddof=0).replace(0.0, 1.0)
    stage_numeric_columns = stage_numeric_feature_columns(MAX_STAGE_COUNT)
    stage_numeric_stats = train_frame[stage_numeric_columns].astype(np.float64)
    stage_numeric_means = stage_numeric_stats.mean(axis=0)
    stage_numeric_stds = stage_numeric_stats.std(axis=0, ddof=0).replace(0.0, 1.0)

    return ModelMetadata(
        numeric_feature_columns=list(NUMERIC_FEATURE_COLUMNS),
        numeric_means=[float(value) for value in numeric_means.tolist()],
        numeric_stds=[float(value) for value in numeric_stds.tolist()],
        numeric_feature_defaults={column: float(numeric_means[column]) for column in NUMERIC_FEATURE_COLUMNS},
        stage_numeric_feature_columns=stage_numeric_columns,
        stage_numeric_means=[float(value) for value in stage_numeric_means.tolist()],
        stage_numeric_stds=[float(value) for value in stage_numeric_stds.tolist()],
        stage_numeric_feature_defaults={column: float(stage_numeric_means[column]) for column in stage_numeric_columns},
        propellant_vocab=list(PROPELLANT_VOCAB),
        engine_vocab=[ENGINE_PAD_TOKEN] + sorted(
            value
            for value in pd.unique(
                full_frame[[f"stage_{index + 1}_engine_key" for index in range(MAX_STAGE_COUNT)]].to_numpy().ravel()
            ).tolist()
            if value != ENGINE_PAD_TOKEN
        ),
        architecture_keys=sorted(full_frame["architecture_key"].drop_duplicates().tolist()),
        origin_vocab=build_category_vocab(full_frame["origin"]),
        destination_vocab=build_category_vocab(full_frame["destination"]),
        max_stage_count=MAX_STAGE_COUNT,
        feature_version=6,
    )


class ArchitectureDataset(Dataset):
    def __init__(self, frame: pd.DataFrame, metadata: ModelMetadata):
        propellant_to_id = {key: index for index, key in enumerate(metadata.propellant_vocab)}
        engine_to_id = {key: index for index, key in enumerate(metadata.engine_vocab)}
        origin_to_id = {key: index for index, key in enumerate(metadata.origin_vocab)}
        destination_to_id = {key: index for index, key in enumerate(metadata.destination_vocab)}

        numeric = frame[metadata.numeric_feature_columns].to_numpy(dtype=np.float32, copy=True)
        means = np.asarray(metadata.numeric_means, dtype=np.float32)
        stds = np.asarray(metadata.numeric_stds, dtype=np.float32)
        numeric = (numeric - means) / stds
        if metadata.stage_numeric_feature_columns:
            stage_numeric = frame[metadata.stage_numeric_feature_columns].to_numpy(dtype=np.float32, copy=True)
            stage_numeric_means = np.asarray(metadata.stage_numeric_means, dtype=np.float32)
            stage_numeric_stds = np.asarray(metadata.stage_numeric_stds, dtype=np.float32)
            stage_numeric = (stage_numeric - stage_numeric_means) / stage_numeric_stds
        else:
            stage_numeric = np.zeros((len(frame), 0), dtype=np.float32)

        stage_columns = [f"stage_{index + 1}_propellant_key" for index in range(metadata.max_stage_count)]
        stage_keys = frame[stage_columns].to_numpy(dtype=str, copy=True)
        stage_propellant_ids = np.vectorize(propellant_to_id.__getitem__)(stage_keys).astype(np.int64)
        engine_columns = [f"stage_{index + 1}_engine_key" for index in range(metadata.max_stage_count)]
        engine_keys = frame[engine_columns].fillna(ENGINE_PAD_TOKEN).to_numpy(dtype=str, copy=True)
        stage_engine_ids = np.vectorize(lambda value: engine_to_id.get(value, 0))(engine_keys).astype(np.int64)

        origins = normalize_category_series(frame["origin"]).map(lambda value: origin_to_id.get(value, 0)).to_numpy(dtype=np.int64, copy=True)
        destinations = normalize_category_series(frame["destination"]).map(lambda value: destination_to_id.get(value, 0)).to_numpy(dtype=np.int64, copy=True)

        self.numeric = torch.from_numpy(numeric)
        self.stage_numeric = torch.from_numpy(stage_numeric)
        self.origin_id = torch.from_numpy(origins)
        self.destination_id = torch.from_numpy(destinations)
        self.stage_propellant_ids = torch.from_numpy(stage_propellant_ids)
        self.stage_engine_ids = torch.from_numpy(stage_engine_ids)
        self.stage_count = torch.from_numpy(frame["stage_count"].to_numpy(dtype=np.int64, copy=True))
        training_score_values = frame["training_score_kg_equivalent"].to_numpy(dtype=np.float64, copy=True)
        self.target_score_log = torch.from_numpy(np.log1p(training_score_values).astype(np.float32))
        self.target_launch_mass_log = torch.from_numpy(np.log1p(frame["launch_mass_kg"].to_numpy(dtype=np.float64, copy=True)).astype(np.float32))
        self.target_boiloff_log = torch.from_numpy(np.log1p(frame["total_boiloff_mass_kg"].to_numpy(dtype=np.float64, copy=True)).astype(np.float32))
        self.target_dry_mass_log = torch.from_numpy(np.log1p(frame["total_dry_mass_kg"].to_numpy(dtype=np.float64, copy=True)).astype(np.float32))
        self.target_tank_mass_log = torch.from_numpy(np.log1p(frame["total_tank_mass_kg"].to_numpy(dtype=np.float64, copy=True)).astype(np.float32))
        self.target_tank_carry_log = torch.from_numpy(np.log1p(frame["tank_carry_proxy_kg"].to_numpy(dtype=np.float64, copy=True)).astype(np.float32))
        stage_dv_columns = [f"stage_{index + 1}_dv_mps" for index in range(metadata.max_stage_count)]
        stage_dv_values = frame[stage_dv_columns].fillna(0.0).to_numpy(dtype=np.float32, copy=True)
        stage_storage_columns = [f"stage_{index + 1}_storage_days" for index in range(metadata.max_stage_count)]
        stage_storage_values = frame[stage_storage_columns].fillna(0.0).to_numpy(dtype=np.float32, copy=True)
        stage_tank_columns = [f"stage_{index + 1}_tank_mass_kg" for index in range(metadata.max_stage_count)]
        stage_tank_values = frame[stage_tank_columns].fillna(0.0).to_numpy(dtype=np.float32, copy=True)
        stage_total_dv = np.clip(stage_dv_values.sum(axis=1, keepdims=True), 1.0, None)
        stage_dv_share = stage_dv_values / stage_total_dv
        stage_total_tank = np.clip(stage_tank_values.sum(axis=1, keepdims=True), 1.0, None)
        stage_tank_share = stage_tank_values / stage_total_tank
        stage_count_values = frame["stage_count"].to_numpy(dtype=np.int64, copy=True)
        stage_mask = (
            np.arange(metadata.max_stage_count, dtype=np.int64)[None, :]
            < stage_count_values[:, None]
        ).astype(np.float32)
        stage_dv_share *= stage_mask
        stage_tank_share *= stage_mask

        self.target_stage_dv_share = torch.from_numpy(stage_dv_share.astype(np.float32))
        self.target_stage_tank_share = torch.from_numpy(stage_tank_share.astype(np.float32))
        self.actual_stage_dv_mps = torch.from_numpy(stage_dv_values.astype(np.float32))
        self.actual_stage_total_dv_mps = torch.from_numpy(stage_dv_values.sum(axis=1).astype(np.float32))
        self.actual_stage_tank_mass_kg = torch.from_numpy(stage_tank_values.astype(np.float32))
        self.stage_storage_days = torch.from_numpy(stage_storage_values.astype(np.float32))
        score_by_mission = frame.groupby("mission_id")["training_score_kg_equivalent"].transform("min").to_numpy(dtype=np.float64, copy=True)
        score_regret = training_score_values / np.clip(score_by_mission, 1.0, None)
        proximity_weight = 1.0 + (1.75 * np.exp(-(score_regret - 1.0) * 5.0))
        rank_weight = 1.0 + (2.0 / np.sqrt(frame["output_rank"].to_numpy(dtype=np.float64, copy=True)))
        self.sample_weight = torch.from_numpy((rank_weight * proximity_weight).astype(np.float32))
        self.mission_id = torch.from_numpy(frame["mission_id"].to_numpy(dtype=np.int64, copy=True))
        self.actual_score_kg = torch.from_numpy(training_score_values.astype(np.float32))
        self.actual_launch_mass_kg = torch.from_numpy(frame["launch_mass_kg"].to_numpy(dtype=np.float32, copy=True))
        self.actual_boiloff_kg = torch.from_numpy(frame["total_boiloff_mass_kg"].to_numpy(dtype=np.float32, copy=True))
        self.actual_tank_mass_kg = torch.from_numpy(frame["total_tank_mass_kg"].to_numpy(dtype=np.float32, copy=True))
        self.actual_tank_carry_kg = torch.from_numpy(frame["tank_carry_proxy_kg"].to_numpy(dtype=np.float32, copy=True))
        self.actual_dry_mass_kg = torch.from_numpy(frame["total_dry_mass_kg"].to_numpy(dtype=np.float32, copy=True))
        self.architecture_keys = frame["architecture_key"].astype(str).tolist()
        self.tensor_fields = (
            "numeric",
            "stage_numeric",
            "origin_id",
            "destination_id",
            "stage_propellant_ids",
            "stage_engine_ids",
            "stage_count",
            "target_score_log",
            "target_launch_mass_log",
            "target_boiloff_log",
            "target_dry_mass_log",
            "target_tank_mass_log",
            "target_tank_carry_log",
            "target_stage_dv_share",
            "target_stage_tank_share",
            "stage_storage_days",
            "sample_weight",
            "mission_id",
            "actual_score_kg",
            "actual_launch_mass_kg",
            "actual_boiloff_kg",
            "actual_tank_mass_kg",
            "actual_tank_carry_kg",
            "actual_dry_mass_kg",
            "actual_stage_dv_mps",
            "actual_stage_tank_mass_kg",
            "actual_stage_total_dv_mps",
        )

    def __len__(self) -> int:
        return self.numeric.shape[0]

    def __getitem__(self, index: int) -> dict[str, Any]:
        return {
            "numeric": self.numeric[index],
            "stage_numeric": self.stage_numeric[index],
            "origin_id": self.origin_id[index],
            "destination_id": self.destination_id[index],
            "stage_propellant_ids": self.stage_propellant_ids[index],
            "stage_engine_ids": self.stage_engine_ids[index],
            "stage_count": self.stage_count[index],
            "target_score_log": self.target_score_log[index],
            "target_launch_mass_log": self.target_launch_mass_log[index],
            "target_boiloff_log": self.target_boiloff_log[index],
            "target_dry_mass_log": self.target_dry_mass_log[index],
            "target_tank_mass_log": self.target_tank_mass_log[index],
            "target_tank_carry_log": self.target_tank_carry_log[index],
            "target_stage_dv_share": self.target_stage_dv_share[index],
            "target_stage_tank_share": self.target_stage_tank_share[index],
            "stage_storage_days": self.stage_storage_days[index],
            "sample_weight": self.sample_weight[index],
            "mission_id": self.mission_id[index],
            "actual_score_kg": self.actual_score_kg[index],
            "actual_launch_mass_kg": self.actual_launch_mass_kg[index],
            "actual_boiloff_kg": self.actual_boiloff_kg[index],
            "actual_tank_mass_kg": self.actual_tank_mass_kg[index],
            "actual_tank_carry_kg": self.actual_tank_carry_kg[index],
            "actual_dry_mass_kg": self.actual_dry_mass_kg[index],
            "actual_stage_dv_mps": self.actual_stage_dv_mps[index],
            "actual_stage_tank_mass_kg": self.actual_stage_tank_mass_kg[index],
            "actual_stage_total_dv_mps": self.actual_stage_total_dv_mps[index],
            "architecture_key": self.architecture_keys[index],
        }

    def get_batch(self, indices: torch.Tensor) -> dict[str, Any]:
        batch = {field: getattr(self, field)[indices] for field in self.tensor_fields}
        batch["architecture_key"] = [self.architecture_keys[int(index)] for index in indices.tolist()]
        return batch
