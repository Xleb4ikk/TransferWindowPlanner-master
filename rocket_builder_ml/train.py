from __future__ import annotations

import argparse
from pathlib import Path
import json
import math
import random

import numpy as np
import pandas as pd
import torch
import torch.nn.functional as F
from torch import nn

from .data import (
    ArchitectureDataset,
    PROPELLANT_VOCAB,
    build_model_metadata,
    load_architecture_training_frame,
    split_by_mission,
)
from .model import ArchitectureRanker

PROPELLANT_TO_ID = {key: index for index, key in enumerate(PROPELLANT_VOCAB)}
LONG_COAST_CRYO_EXCESS_LOSS_WEIGHT = 0.28
CRYO_STORAGE_DAYS_START = 90.0
CRYO_STORAGE_DAYS_SPAN = 150.0
CRYO_PROP_RISK = {
    PROPELLANT_TO_ID["lox_lh2"]: 1.00,
    PROPELLANT_TO_ID["lox_ch4"]: 0.45,
}


def parse_hidden_dims(value: str) -> tuple[int, ...]:
    dims = tuple(int(part.strip()) for part in value.split(",") if part.strip())
    if not dims:
        raise argparse.ArgumentTypeError("hidden dims must contain at least one integer")
    return dims


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train a neural ranker that chooses rocket architectures for a mission.")
    parser.add_argument("--dataset-dir", required=True, help="Directory containing mission_dataset.csv and architecture_dataset.csv.")
    parser.add_argument("--output-dir", required=True, help="Where model.pt, metadata.json, and metrics.json will be saved.")
    parser.add_argument("--epochs", type=int, default=72)
    parser.add_argument("--batch-size", type=int, default=4096)
    parser.add_argument("--lr", type=float, default=5e-4)
    parser.add_argument("--min-lr", type=float, default=5e-5)
    parser.add_argument("--weight-decay", type=float, default=1e-4)
    parser.add_argument("--dropout", type=float, default=0.08)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--device", default="auto", choices=["auto", "cpu", "cuda"])
    parser.add_argument("--num-workers", type=int, default=0)
    parser.add_argument("--patience", type=int, default=14)
    parser.add_argument("--max-missions", type=int, default=0, help="Optional cap for quick experiments or smoke tests.")
    parser.add_argument("--hidden-dims", type=parse_hidden_dims, default=(1024, 768, 512, 384, 256))
    parser.add_argument("--propellant-embedding-dim", type=int, default=40)
    parser.add_argument("--engine-embedding-dim", type=int, default=24)
    parser.add_argument("--stage-count-embedding-dim", type=int, default=20)
    parser.add_argument("--mission-body-embedding-dim", type=int, default=24)
    parser.add_argument("--grad-clip", type=float, default=1.0)
    parser.add_argument("--warmup-epochs", type=int, default=4)
    parser.add_argument("--amp", default="auto", choices=["auto", "on", "off"])
    return parser.parse_args()


def set_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def select_device(requested: str) -> torch.device:
    if requested == "cpu":
        return torch.device("cpu")
    if requested == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA was requested, but torch.cuda.is_available() is False.")
        return torch.device("cuda")
    if torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


def amp_enabled(device: torch.device, amp_mode: str) -> bool:
    if amp_mode == "on":
        return device.type == "cuda"
    if amp_mode == "off":
        return False
    return device.type == "cuda"


def build_lr_lambda(total_epochs: int, warmup_epochs: int, min_lr_ratio: float):
    total_epochs = max(1, total_epochs)
    warmup_epochs = max(0, min(warmup_epochs, total_epochs - 1))

    def schedule(epoch_index: int) -> float:
        step = epoch_index + 1
        if warmup_epochs > 0 and step <= warmup_epochs:
            return step / warmup_epochs

        if total_epochs <= warmup_epochs + 1:
            return 1.0

        progress = (step - warmup_epochs) / max(1, total_epochs - warmup_epochs)
        cosine = 0.5 * (1.0 + math.cos(math.pi * progress))
        return min_lr_ratio + ((1.0 - min_lr_ratio) * cosine)

    return schedule


def maybe_limit_missions(frame: pd.DataFrame, max_missions: int) -> pd.DataFrame:
    if max_missions <= 0:
        return frame

    mission_ids = frame["mission_id"].drop_duplicates().sort_values().tolist()
    if max_missions >= len(mission_ids):
        return frame

    keep_ids = set(mission_ids[:max_missions])
    return frame[frame["mission_id"].isin(keep_ids)].reset_index(drop=True)


def move_batch_to_device(batch: dict[str, object], device: torch.device) -> dict[str, object]:
    moved: dict[str, object] = {}
    for key, value in batch.items():
        if isinstance(value, torch.Tensor):
            moved[key] = value.to(device, non_blocking=device.type == "cuda")
        else:
            moved[key] = value
    return moved


def iterate_batches(dataset: ArchitectureDataset, batch_size: int, shuffle: bool) -> tuple[int, dict[str, object]]:
    if shuffle:
        indices = torch.randperm(len(dataset))
    else:
        indices = torch.arange(len(dataset))

    for start in range(0, len(dataset), batch_size):
        stop = min(start + batch_size, len(dataset))
        batch_indices = indices[start:stop]
        yield stop - start, dataset.get_batch(batch_indices)


def compute_long_coast_cryo_stage_risk(
    stage_propellant_ids: torch.Tensor,
    stage_storage_days: torch.Tensor,
) -> torch.Tensor:
    propellant_risk = torch.zeros_like(stage_storage_days, dtype=torch.float32)
    for propellant_id, risk_value in CRYO_PROP_RISK.items():
        propellant_risk = torch.where(
            stage_propellant_ids.to(torch.long) == propellant_id,
            torch.full_like(propellant_risk, risk_value),
            propellant_risk,
        )

    storage_risk = ((stage_storage_days - CRYO_STORAGE_DAYS_START) / CRYO_STORAGE_DAYS_SPAN).clamp(0.0, 1.0)
    return propellant_risk * storage_risk


def compute_long_coast_cryo_excess_regularizer(
    outputs: dict[str, torch.Tensor],
    batch: dict[str, object],
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor]:
    predicted_stage_dv = outputs["stage_dv_share"] * batch["actual_stage_total_dv_mps"].unsqueeze(1)
    cryo_stage_excess_dv = torch.relu(predicted_stage_dv - batch["actual_stage_dv_mps"])
    cryo_stage_risk = compute_long_coast_cryo_stage_risk(
        batch["stage_propellant_ids"],
        batch["stage_storage_days"],
    )
    valid_stage_mask = (
        torch.arange(predicted_stage_dv.shape[1], device=predicted_stage_dv.device).unsqueeze(0)
        < batch["stage_count"].unsqueeze(1)
    ).to(predicted_stage_dv.dtype)
    cryo_stage_risk = cryo_stage_risk * valid_stage_mask
    cryo_excess_loss = F.huber_loss(
        cryo_stage_excess_dv / 1000.0,
        torch.zeros_like(cryo_stage_excess_dv),
        reduction="none",
    ) * cryo_stage_risk
    return cryo_excess_loss.sum(dim=1), predicted_stage_dv, cryo_stage_excess_dv, cryo_stage_risk


def compute_losses(outputs: dict[str, torch.Tensor], batch: dict[str, object]) -> tuple[torch.Tensor, dict[str, float]]:
    score_loss = F.huber_loss(outputs["score_log"], batch["target_score_log"], reduction="none")
    launch_mass_loss = F.huber_loss(outputs["launch_mass_log"], batch["target_launch_mass_log"], reduction="none")
    boiloff_loss = F.huber_loss(outputs["boiloff_log"], batch["target_boiloff_log"], reduction="none")
    dry_mass_loss = F.huber_loss(outputs["dry_mass_log"], batch["target_dry_mass_log"], reduction="none")
    tank_mass_loss = F.huber_loss(outputs["tank_mass_log"], batch["target_tank_mass_log"], reduction="none")
    tank_carry_loss = F.huber_loss(outputs["tank_carry_log"], batch["target_tank_carry_log"], reduction="none")
    stage_share_loss = F.huber_loss(outputs["stage_dv_share"], batch["target_stage_dv_share"], reduction="none")
    stage_share_loss = stage_share_loss.sum(dim=1) / batch["stage_count"].clamp(min=1).to(stage_share_loss.dtype)
    stage_tank_share_loss = F.huber_loss(outputs["stage_tank_share"], batch["target_stage_tank_share"], reduction="none")
    stage_tank_share_loss = stage_tank_share_loss.sum(dim=1) / batch["stage_count"].clamp(min=1).to(stage_tank_share_loss.dtype)
    cryo_excess_loss, _, cryo_stage_excess_dv, cryo_stage_risk = compute_long_coast_cryo_excess_regularizer(outputs, batch)

    total_loss = (
        score_loss
        + (0.26 * launch_mass_loss)
        + (0.12 * boiloff_loss)
        + (0.18 * dry_mass_loss)
        + (0.16 * tank_mass_loss)
        + (0.12 * tank_carry_loss)
        + (0.18 * stage_share_loss)
        + (0.10 * stage_tank_share_loss)
        + (LONG_COAST_CRYO_EXCESS_LOSS_WEIGHT * cryo_excess_loss)
    ) * batch["sample_weight"]
    reduced = total_loss.mean()
    risky_mask = cryo_stage_risk > 1e-6
    if risky_mask.any():
        risky_excess_dv_mps = float(cryo_stage_excess_dv[risky_mask].mean().detach().cpu())
    else:
        risky_excess_dv_mps = 0.0

    return reduced, {
        "score_loss": float(score_loss.mean().detach().cpu()),
        "launch_mass_loss": float(launch_mass_loss.mean().detach().cpu()),
        "boiloff_loss": float(boiloff_loss.mean().detach().cpu()),
        "dry_mass_loss": float(dry_mass_loss.mean().detach().cpu()),
        "tank_mass_loss": float(tank_mass_loss.mean().detach().cpu()),
        "tank_carry_loss": float(tank_carry_loss.mean().detach().cpu()),
        "stage_share_loss": float(stage_share_loss.mean().detach().cpu()),
        "stage_tank_share_loss": float(stage_tank_share_loss.mean().detach().cpu()),
        "cryo_excess_loss": float(cryo_excess_loss.mean().detach().cpu()),
        "risky_cryo_excess_dv_mps": risky_excess_dv_mps,
    }


def model_forward(model: nn.Module, batch: dict[str, object]) -> dict[str, torch.Tensor]:
    return model(
        batch["numeric"],
        batch["origin_id"],
        batch["destination_id"],
        batch["stage_propellant_ids"],
        batch["stage_count"],
        batch.get("stage_engine_ids"),
        batch.get("stage_numeric"),
    )


def train_epoch(
    model: nn.Module,
    dataset: ArchitectureDataset,
    batch_size: int,
    optimizer: torch.optim.Optimizer,
    device: torch.device,
    grad_clip: float,
    scaler: torch.cuda.amp.GradScaler | None,
    use_amp: bool,
) -> dict[str, float]:
    model.train()
    total_loss = 0.0
    total_score_loss = 0.0
    total_launch_loss = 0.0
    total_boiloff_loss = 0.0
    total_dry_loss = 0.0
    total_tank_loss = 0.0
    total_tank_carry_loss = 0.0
    total_stage_share_loss = 0.0
    total_stage_tank_share_loss = 0.0
    total_cryo_excess_loss = 0.0
    step_count = 0

    for _, batch in iterate_batches(dataset, batch_size, shuffle=True):
        batch = move_batch_to_device(batch, device)
        optimizer.zero_grad(set_to_none=True)
        with torch.autocast(device_type=device.type, dtype=torch.float16, enabled=use_amp):
            outputs = model_forward(model, batch)
            loss, parts = compute_losses(outputs, batch)
        if scaler is not None and use_amp:
            scaler.scale(loss).backward()
            if grad_clip > 0:
                scaler.unscale_(optimizer)
                torch.nn.utils.clip_grad_norm_(model.parameters(), grad_clip)
            scaler.step(optimizer)
            scaler.update()
        else:
            loss.backward()
            if grad_clip > 0:
                torch.nn.utils.clip_grad_norm_(model.parameters(), grad_clip)
            optimizer.step()

        total_loss += float(loss.detach().cpu())
        total_score_loss += parts["score_loss"]
        total_launch_loss += parts["launch_mass_loss"]
        total_boiloff_loss += parts["boiloff_loss"]
        total_dry_loss += parts["dry_mass_loss"]
        total_tank_loss += parts["tank_mass_loss"]
        total_tank_carry_loss += parts["tank_carry_loss"]
        total_stage_share_loss += parts["stage_share_loss"]
        total_stage_tank_share_loss += parts["stage_tank_share_loss"]
        total_cryo_excess_loss += parts["cryo_excess_loss"]
        step_count += 1

    return {
        "loss": total_loss / max(1, step_count),
        "score_loss": total_score_loss / max(1, step_count),
        "launch_mass_loss": total_launch_loss / max(1, step_count),
        "boiloff_loss": total_boiloff_loss / max(1, step_count),
        "dry_mass_loss": total_dry_loss / max(1, step_count),
        "tank_mass_loss": total_tank_loss / max(1, step_count),
        "tank_carry_loss": total_tank_carry_loss / max(1, step_count),
        "stage_share_loss": total_stage_share_loss / max(1, step_count),
        "stage_tank_share_loss": total_stage_tank_share_loss / max(1, step_count),
        "cryo_excess_loss": total_cryo_excess_loss / max(1, step_count),
    }


@torch.no_grad()
def evaluate(model: nn.Module, dataset: ArchitectureDataset, batch_size: int, device: torch.device) -> dict[str, float]:
    model.eval()
    total_loss = 0.0
    row_count = 0

    predicted_score_logs: list[np.ndarray] = []
    predicted_launch_logs: list[np.ndarray] = []
    predicted_boiloff_logs: list[np.ndarray] = []
    predicted_dry_logs: list[np.ndarray] = []
    predicted_tank_logs: list[np.ndarray] = []
    predicted_tank_carry_logs: list[np.ndarray] = []
    predicted_stage_shares: list[np.ndarray] = []
    predicted_stage_tank_shares: list[np.ndarray] = []
    true_score_logs: list[np.ndarray] = []
    true_launch_logs: list[np.ndarray] = []
    true_boiloff_logs: list[np.ndarray] = []
    true_dry_logs: list[np.ndarray] = []
    true_tank_logs: list[np.ndarray] = []
    true_tank_carry_logs: list[np.ndarray] = []
    true_stage_shares: list[np.ndarray] = []
    true_stage_tank_shares: list[np.ndarray] = []
    mission_ids: list[np.ndarray] = []
    actual_scores: list[np.ndarray] = []
    actual_stage_dvs: list[np.ndarray] = []
    actual_stage_tanks: list[np.ndarray] = []
    actual_stage_total_dvs: list[np.ndarray] = []
    stage_counts: list[np.ndarray] = []
    predicted_stage_dvs: list[np.ndarray] = []
    cryo_stage_excess_dvs: list[np.ndarray] = []
    cryo_stage_risks: list[np.ndarray] = []
    architecture_keys: list[str] = []

    for current_batch_size, batch in iterate_batches(dataset, batch_size, shuffle=False):
        batch = move_batch_to_device(batch, device)
        outputs = model_forward(model, batch)
        loss, _ = compute_losses(outputs, batch)
        _, predicted_stage_dv, cryo_stage_excess_dv, cryo_stage_risk = compute_long_coast_cryo_excess_regularizer(outputs, batch)
        total_loss += float(loss.detach().cpu()) * current_batch_size
        row_count += current_batch_size

        predicted_score_logs.append(outputs["score_log"].detach().cpu().numpy())
        predicted_launch_logs.append(outputs["launch_mass_log"].detach().cpu().numpy())
        predicted_boiloff_logs.append(outputs["boiloff_log"].detach().cpu().numpy())
        predicted_dry_logs.append(outputs["dry_mass_log"].detach().cpu().numpy())
        predicted_tank_logs.append(outputs["tank_mass_log"].detach().cpu().numpy())
        predicted_tank_carry_logs.append(outputs["tank_carry_log"].detach().cpu().numpy())
        predicted_stage_shares.append(outputs["stage_dv_share"].detach().cpu().numpy())
        predicted_stage_tank_shares.append(outputs["stage_tank_share"].detach().cpu().numpy())
        true_score_logs.append(batch["target_score_log"].detach().cpu().numpy())
        true_launch_logs.append(batch["target_launch_mass_log"].detach().cpu().numpy())
        true_boiloff_logs.append(batch["target_boiloff_log"].detach().cpu().numpy())
        true_dry_logs.append(batch["target_dry_mass_log"].detach().cpu().numpy())
        true_tank_logs.append(batch["target_tank_mass_log"].detach().cpu().numpy())
        true_tank_carry_logs.append(batch["target_tank_carry_log"].detach().cpu().numpy())
        true_stage_shares.append(batch["target_stage_dv_share"].detach().cpu().numpy())
        true_stage_tank_shares.append(batch["target_stage_tank_share"].detach().cpu().numpy())
        mission_ids.append(batch["mission_id"].detach().cpu().numpy())
        actual_scores.append(batch["actual_score_kg"].detach().cpu().numpy())
        actual_stage_dvs.append(batch["actual_stage_dv_mps"].detach().cpu().numpy())
        actual_stage_tanks.append(batch["actual_stage_tank_mass_kg"].detach().cpu().numpy())
        actual_stage_total_dvs.append(batch["actual_stage_total_dv_mps"].detach().cpu().numpy())
        stage_counts.append(batch["stage_count"].detach().cpu().numpy())
        predicted_stage_dvs.append(predicted_stage_dv.detach().cpu().numpy())
        cryo_stage_excess_dvs.append(cryo_stage_excess_dv.detach().cpu().numpy())
        cryo_stage_risks.append(cryo_stage_risk.detach().cpu().numpy())
        architecture_keys.extend(batch["architecture_key"])

    pred_score_log = np.concatenate(predicted_score_logs, axis=0)
    pred_launch_log = np.concatenate(predicted_launch_logs, axis=0)
    pred_boiloff_log = np.concatenate(predicted_boiloff_logs, axis=0)
    pred_dry_log = np.concatenate(predicted_dry_logs, axis=0)
    pred_tank_log = np.concatenate(predicted_tank_logs, axis=0)
    pred_tank_carry_log = np.concatenate(predicted_tank_carry_logs, axis=0)
    pred_stage_share = np.concatenate(predicted_stage_shares, axis=0)
    pred_stage_tank_share = np.concatenate(predicted_stage_tank_shares, axis=0)
    target_score_log = np.concatenate(true_score_logs, axis=0)
    target_launch_log = np.concatenate(true_launch_logs, axis=0)
    target_boiloff_log = np.concatenate(true_boiloff_logs, axis=0)
    target_dry_log = np.concatenate(true_dry_logs, axis=0)
    target_tank_log = np.concatenate(true_tank_logs, axis=0)
    target_tank_carry_log = np.concatenate(true_tank_carry_logs, axis=0)
    target_stage_share = np.concatenate(true_stage_shares, axis=0)
    target_stage_tank_share = np.concatenate(true_stage_tank_shares, axis=0)
    mission_id_values = np.concatenate(mission_ids, axis=0)
    actual_score_values = np.concatenate(actual_scores, axis=0)
    actual_stage_dv = np.concatenate(actual_stage_dvs, axis=0)
    actual_stage_tank = np.concatenate(actual_stage_tanks, axis=0)
    actual_stage_total_dv = np.concatenate(actual_stage_total_dvs, axis=0)
    stage_count_values = np.concatenate(stage_counts, axis=0)
    predicted_stage_dv_values = np.concatenate(predicted_stage_dvs, axis=0)
    cryo_stage_excess_dv_values = np.concatenate(cryo_stage_excess_dvs, axis=0)
    cryo_stage_risk_values = np.concatenate(cryo_stage_risks, axis=0)
    valid_stage_mask = np.arange(pred_stage_share.shape[1])[None, :] < stage_count_values[:, None]
    predicted_stage_dv = pred_stage_share * actual_stage_total_dv[:, None]
    actual_stage_total_tank = np.clip(actual_stage_tank.sum(axis=1, keepdims=True), 1.0, None)
    predicted_stage_tank = pred_stage_tank_share * actual_stage_total_tank
    risky_cryo_stage_mask = cryo_stage_risk_values > 1e-6

    eval_frame = pd.DataFrame(
        {
            "mission_id": mission_id_values,
            "architecture_key": architecture_keys,
            "actual_score_kg": actual_score_values,
            "pred_score_kg": np.expm1(pred_score_log.astype(np.float64)),
        }
    )

    predicted_best = eval_frame.loc[eval_frame.groupby("mission_id")["pred_score_kg"].idxmin()].copy()
    actual_best = eval_frame.loc[eval_frame.groupby("mission_id")["actual_score_kg"].idxmin()].copy()
    comparison = predicted_best.merge(
        actual_best[["mission_id", "architecture_key", "actual_score_kg"]],
        on="mission_id",
        suffixes=("_pred", "_best"),
    )

    top1_accuracy = float((comparison["architecture_key_pred"] == comparison["architecture_key_best"]).mean())
    regret = comparison["actual_score_kg_pred"] / comparison["actual_score_kg_best"]

    return {
        "loss": total_loss / max(1, row_count),
        "score_log_mae": float(np.mean(np.abs(pred_score_log - target_score_log))),
        "launch_mass_log_mae": float(np.mean(np.abs(pred_launch_log - target_launch_log))),
        "boiloff_log_mae": float(np.mean(np.abs(pred_boiloff_log - target_boiloff_log))),
        "dry_mass_log_mae": float(np.mean(np.abs(pred_dry_log - target_dry_log))),
        "tank_mass_log_mae": float(np.mean(np.abs(pred_tank_log - target_tank_log))),
        "tank_carry_log_mae": float(np.mean(np.abs(pred_tank_carry_log - target_tank_carry_log))),
        "stage_share_mae": float(np.mean(np.abs(pred_stage_share - target_stage_share)[valid_stage_mask])),
        "stage_tank_share_mae": float(np.mean(np.abs(pred_stage_tank_share - target_stage_tank_share)[valid_stage_mask])),
        "stage_dv_mae_mps": float(np.mean(np.abs(predicted_stage_dv - actual_stage_dv)[valid_stage_mask])),
        "stage_tank_mae_kg": float(np.mean(np.abs(predicted_stage_tank - actual_stage_tank)[valid_stage_mask])),
        "long_coast_cryo_excess_dv_mps": float(cryo_stage_excess_dv_values[risky_cryo_stage_mask].mean()) if risky_cryo_stage_mask.any() else 0.0,
        "long_coast_cryo_oversize_rate": float(np.mean((cryo_stage_excess_dv_values[risky_cryo_stage_mask] > 150.0).astype(np.float64))) if risky_cryo_stage_mask.any() else 0.0,
        "top1_accuracy": top1_accuracy,
        "mean_regret": float(regret.mean()),
        "median_regret": float(regret.median()),
    }


def save_model(output_dir: Path, model: ArchitectureRanker, metadata, args: argparse.Namespace) -> None:
    checkpoint = {
        "model_state_dict": model.state_dict(),
        "model_config": {
            "numeric_dim": len(metadata.numeric_feature_columns),
            "propellant_vocab_size": len(metadata.propellant_vocab),
            "stage_slots": metadata.max_stage_count,
            "engine_vocab_size": len(metadata.engine_vocab),
            "origin_vocab_size": len(metadata.origin_vocab),
            "destination_vocab_size": len(metadata.destination_vocab),
            "hidden_dims": tuple(args.hidden_dims),
            "propellant_embedding_dim": args.propellant_embedding_dim,
            "engine_embedding_dim": args.engine_embedding_dim,
            "stage_count_embedding_dim": args.stage_count_embedding_dim,
            "mission_body_embedding_dim": args.mission_body_embedding_dim,
            "stage_numeric_dim": len(metadata.stage_numeric_feature_columns),
            "dropout": args.dropout,
        },
    }
    torch.save(checkpoint, output_dir / "model.pt")


def main() -> None:
    args = parse_args()
    set_seed(args.seed)
    if hasattr(torch, "set_float32_matmul_precision"):
        torch.set_float32_matmul_precision("high")

    device = select_device(args.device)
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)

    full_frame = load_architecture_training_frame(args.dataset_dir)
    full_frame = maybe_limit_missions(full_frame, args.max_missions)
    splits = split_by_mission(full_frame, seed=args.seed)
    metadata = build_model_metadata(splits.train, full_frame)
    metadata.save(output_dir / "metadata.json")

    train_dataset = ArchitectureDataset(splits.train, metadata)
    val_dataset = ArchitectureDataset(splits.val, metadata)
    test_dataset = ArchitectureDataset(splits.test, metadata)

    model = ArchitectureRanker(
        numeric_dim=len(metadata.numeric_feature_columns),
        propellant_vocab_size=len(metadata.propellant_vocab),
        stage_slots=metadata.max_stage_count,
        engine_vocab_size=len(metadata.engine_vocab),
        origin_vocab_size=len(metadata.origin_vocab),
        destination_vocab_size=len(metadata.destination_vocab),
        hidden_dims=tuple(args.hidden_dims),
        propellant_embedding_dim=args.propellant_embedding_dim,
        engine_embedding_dim=args.engine_embedding_dim,
        stage_count_embedding_dim=args.stage_count_embedding_dim,
        mission_body_embedding_dim=args.mission_body_embedding_dim,
        stage_numeric_dim=len(metadata.stage_numeric_feature_columns),
        dropout=args.dropout,
    ).to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=args.weight_decay)
    min_lr_ratio = min(1.0, max(0.0, args.min_lr / max(args.lr, 1e-9)))
    lr_schedule = build_lr_lambda(args.epochs, args.warmup_epochs, min_lr_ratio)
    use_amp = amp_enabled(device, args.amp)
    scaler = torch.amp.GradScaler(device.type, enabled=use_amp and device.type == "cuda")

    history: list[dict[str, float]] = []
    best_state: dict[str, torch.Tensor] | None = None
    best_epoch = 0
    best_metrics: dict[str, float] | None = None
    bad_epoch_count = 0

    print(
        f"Training on {device.type} with "
        f"{splits.train['mission_id'].nunique()} train missions, "
        f"{splits.val['mission_id'].nunique()} val missions, "
        f"{splits.test['mission_id'].nunique()} test missions."
    )
    print(
        f"Rows: train={len(train_dataset):,}, val={len(val_dataset):,}, test={len(test_dataset):,}, "
        f"architectures={len(metadata.architecture_keys)}, engines={len(metadata.engine_vocab) - 1}, origins={len(metadata.origin_vocab) - 1}, "
        f"destinations={len(metadata.destination_vocab) - 1}"
    )
    print(
        f"Model: hidden_dims={tuple(args.hidden_dims)}, prop_emb={args.propellant_embedding_dim}, engine_emb={args.engine_embedding_dim}, "
        f"body_emb={args.mission_body_embedding_dim}, stage_numeric={len(metadata.stage_numeric_feature_columns)}, "
        f"batch_size={args.batch_size}, lr={args.lr}, min_lr={args.min_lr}, amp={use_amp}"
    )

    for epoch in range(1, args.epochs + 1):
        current_lr = args.lr * lr_schedule(epoch - 1)
        for group in optimizer.param_groups:
            group["lr"] = current_lr
        train_metrics = train_epoch(model, train_dataset, args.batch_size, optimizer, device, args.grad_clip, scaler, use_amp)
        val_metrics = evaluate(model, val_dataset, args.batch_size, device)
        history.append(
            {
                "epoch": epoch,
                "train_loss": train_metrics["loss"],
                "train_score_loss": train_metrics["score_loss"],
                "train_stage_share_loss": train_metrics["stage_share_loss"],
                "train_stage_tank_share_loss": train_metrics["stage_tank_share_loss"],
                "train_tank_mass_loss": train_metrics["tank_mass_loss"],
                "train_tank_carry_loss": train_metrics["tank_carry_loss"],
                "train_cryo_excess_loss": train_metrics["cryo_excess_loss"],
                "val_loss": val_metrics["loss"],
                "val_score_log_mae": val_metrics["score_log_mae"],
                "val_stage_dv_mae_mps": val_metrics["stage_dv_mae_mps"],
                "val_stage_tank_mae_kg": val_metrics["stage_tank_mae_kg"],
                "val_tank_mass_log_mae": val_metrics["tank_mass_log_mae"],
                "val_long_coast_cryo_excess_dv_mps": val_metrics["long_coast_cryo_excess_dv_mps"],
                "val_top1_accuracy": val_metrics["top1_accuracy"],
                "val_mean_regret": val_metrics["mean_regret"],
                "lr": float(current_lr),
            }
        )

        print(
            f"epoch {epoch:03d} | "
            f"train_loss={train_metrics['loss']:.4f} | "
            f"val_loss={val_metrics['loss']:.4f} | "
            f"val_stage_dv_mae={val_metrics['stage_dv_mae_mps']:.1f} | "
            f"val_tank_mae={val_metrics['stage_tank_mae_kg']:.1f} | "
            f"val_long_coast_cryo_excess={val_metrics['long_coast_cryo_excess_dv_mps']:.1f} | "
            f"val_top1={val_metrics['top1_accuracy']:.4f} | "
            f"val_regret={val_metrics['mean_regret']:.4f} | "
            f"lr={current_lr:.6f}"
        )

        is_better = False
        if best_metrics is None:
            is_better = True
        else:
            current_accuracy = val_metrics["top1_accuracy"]
            best_accuracy = best_metrics["top1_accuracy"]
            current_regret = val_metrics["mean_regret"]
            best_regret = best_metrics["mean_regret"]
            current_loss = val_metrics["loss"]
            best_loss = best_metrics["loss"]
            if current_regret < best_regret - 1e-4:
                is_better = True
            elif abs(current_regret - best_regret) <= 1e-4 and current_accuracy > best_accuracy + 1e-4:
                is_better = True
            elif abs(current_regret - best_regret) <= 1e-4 and abs(current_accuracy - best_accuracy) <= 1e-4 and current_loss < best_loss - 1e-4:
                is_better = True

        if is_better:
            best_state = {name: tensor.detach().cpu().clone() for name, tensor in model.state_dict().items()}
            best_metrics = val_metrics
            best_epoch = epoch
            bad_epoch_count = 0
        else:
            bad_epoch_count += 1

        if bad_epoch_count >= args.patience:
            print(f"Early stopping after {epoch} epochs without validation improvement.")
            break

    if best_state is None or best_metrics is None:
        raise RuntimeError("Training did not produce a usable checkpoint.")

    model.load_state_dict(best_state)
    test_metrics = evaluate(model, test_dataset, args.batch_size, device)
    save_model(output_dir, model, metadata, args)

    metrics_payload = {
        "config": {
            **vars(args),
            "hidden_dims": list(args.hidden_dims),
        },
        "best_epoch": best_epoch,
        "best_val_metrics": best_metrics,
        "test_metrics": test_metrics,
        "history": history,
        "dataset": {
            "train_rows": len(train_dataset),
            "val_rows": len(val_dataset),
            "test_rows": len(test_dataset),
            "train_missions": int(splits.train["mission_id"].nunique()),
            "val_missions": int(splits.val["mission_id"].nunique()),
            "test_missions": int(splits.test["mission_id"].nunique()),
            "architectures": len(metadata.architecture_keys),
            "origins": len(metadata.origin_vocab) - 1,
            "destinations": len(metadata.destination_vocab) - 1,
        },
        "device": {
            "requested": args.device,
            "selected": device.type,
            "cuda_available": bool(torch.cuda.is_available()),
        },
    }
    (output_dir / "metrics.json").write_text(json.dumps(metrics_payload, indent=2), encoding="utf-8")

    print("Best validation metrics:")
    print(json.dumps(best_metrics, indent=2))
    print("Test metrics:")
    print(json.dumps(test_metrics, indent=2))
    print(f"Artifacts saved to {output_dir}")


if __name__ == "__main__":
    main()
