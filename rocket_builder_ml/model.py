from __future__ import annotations

import torch
from torch import nn


class ResidualMlpBlock(nn.Module):
    def __init__(self, input_dim: int, output_dim: int, dropout: float) -> None:
        super().__init__()
        self.pre = nn.Sequential(
            nn.Linear(input_dim, output_dim),
            nn.LayerNorm(output_dim),
            nn.SiLU(),
            nn.Dropout(dropout),
        )
        self.body = nn.Sequential(
            nn.Linear(output_dim, output_dim),
            nn.LayerNorm(output_dim),
            nn.SiLU(),
            nn.Dropout(dropout),
            nn.Linear(output_dim, output_dim),
            nn.Dropout(dropout),
        )
        self.skip = nn.Identity() if input_dim == output_dim else nn.Linear(input_dim, output_dim)
        self.out_norm = nn.LayerNorm(output_dim)
        self.out_act = nn.SiLU()

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        hidden = self.pre(x)
        residual = self.skip(x)
        hidden = hidden + self.body(hidden) + residual
        return self.out_act(self.out_norm(hidden))


class ArchitectureRanker(nn.Module):
    def __init__(
        self,
        numeric_dim: int,
        propellant_vocab_size: int,
        stage_slots: int,
        engine_vocab_size: int = 1,
        origin_vocab_size: int = 1,
        destination_vocab_size: int = 1,
        hidden_dims: tuple[int, ...] = (768, 512, 320, 192),
        propellant_embedding_dim: int = 32,
        engine_embedding_dim: int = 0,
        stage_count_embedding_dim: int = 12,
        mission_body_embedding_dim: int = 16,
        stage_numeric_dim: int = 0,
        dropout: float = 0.10,
    ) -> None:
        super().__init__()

        self.propellant_embedding = nn.Embedding(propellant_vocab_size, propellant_embedding_dim)
        self.engine_embedding_dim = engine_embedding_dim
        self.engine_embedding = nn.Embedding(engine_vocab_size, engine_embedding_dim) if engine_embedding_dim > 0 else None
        self.stage_position_embedding = nn.Embedding(stage_slots, propellant_embedding_dim)
        self.stage_count_embedding = nn.Embedding(stage_slots + 1, stage_count_embedding_dim)
        self.origin_embedding = nn.Embedding(origin_vocab_size, mission_body_embedding_dim)
        self.destination_embedding = nn.Embedding(destination_vocab_size, mission_body_embedding_dim)
        self.register_buffer("stage_positions", torch.arange(stage_slots, dtype=torch.long), persistent=False)
        self.stage_numeric_dim = stage_numeric_dim

        input_dim = (
            numeric_dim
            + stage_numeric_dim
            + (stage_slots * propellant_embedding_dim)
            + (stage_slots * engine_embedding_dim)
            + stage_count_embedding_dim
            + mission_body_embedding_dim
            + mission_body_embedding_dim
        )

        layers: list[nn.Module] = []
        last_dim = input_dim
        for hidden_dim in hidden_dims:
            layers.append(ResidualMlpBlock(last_dim, hidden_dim, dropout))
            last_dim = hidden_dim

        self.backbone = nn.Sequential(*layers)
        self.output_head = nn.Linear(last_dim, 3)
        self.aux_output_head = nn.Linear(last_dim, 3)
        self.stage_allocation_head = nn.Linear(last_dim, stage_slots)
        self.stage_tank_share_head = nn.Linear(last_dim, stage_slots)

    def forward(
        self,
        numeric: torch.Tensor,
        origin_id: torch.Tensor,
        destination_id: torch.Tensor,
        stage_propellant_ids: torch.Tensor,
        stage_count: torch.Tensor,
        stage_engine_ids: torch.Tensor | None = None,
        stage_numeric: torch.Tensor | None = None,
    ) -> dict[str, torch.Tensor]:
        position_features = self.stage_position_embedding(self.stage_positions.unsqueeze(0))
        propellant_features = self.propellant_embedding(stage_propellant_ids) + position_features
        propellant_features = propellant_features.reshape(stage_propellant_ids.shape[0], -1)
        if self.engine_embedding is not None and stage_engine_ids is not None:
            engine_features = self.engine_embedding(stage_engine_ids).reshape(stage_engine_ids.shape[0], -1)
        elif self.engine_embedding is not None:
            batch_size = stage_propellant_ids.shape[0]
            engine_features = torch.zeros(
                (batch_size, self.stage_position_embedding.num_embeddings * self.engine_embedding_dim),
                dtype=numeric.dtype,
                device=numeric.device,
            )
        else:
            engine_features = None
        stage_count_features = self.stage_count_embedding(stage_count.clamp(min=0, max=self.stage_count_embedding.num_embeddings - 1))
        origin_features = self.origin_embedding(origin_id.clamp(min=0, max=self.origin_embedding.num_embeddings - 1))
        destination_features = self.destination_embedding(destination_id.clamp(min=0, max=self.destination_embedding.num_embeddings - 1))

        parts = [numeric]
        if self.stage_numeric_dim > 0:
            if stage_numeric is None:
                batch_size = numeric.shape[0]
                stage_numeric = torch.zeros((batch_size, self.stage_numeric_dim), dtype=numeric.dtype, device=numeric.device)
            parts.append(stage_numeric)
        parts.extend([origin_features, destination_features, propellant_features])
        if engine_features is not None:
            parts.append(engine_features)
        parts.append(stage_count_features)
        combined = torch.cat(parts, dim=1)
        latent = self.backbone(combined)
        outputs = self.output_head(latent)
        aux_outputs = self.aux_output_head(latent)
        safe_stage_count = stage_count.clamp(min=1, max=self.stage_position_embedding.num_embeddings)
        valid_stage_mask = self.stage_positions.unsqueeze(0) < safe_stage_count.unsqueeze(1)
        stage_share_logits = self.stage_allocation_head(latent)
        masked_stage_share_logits = stage_share_logits.masked_fill(~valid_stage_mask, torch.finfo(stage_share_logits.dtype).min)
        stage_dv_share = torch.softmax(masked_stage_share_logits, dim=1)
        stage_dv_share = torch.where(valid_stage_mask, stage_dv_share, torch.zeros_like(stage_dv_share))
        stage_tank_logits = self.stage_tank_share_head(latent)
        masked_stage_tank_logits = stage_tank_logits.masked_fill(~valid_stage_mask, torch.finfo(stage_tank_logits.dtype).min)
        stage_tank_share = torch.softmax(masked_stage_tank_logits, dim=1)
        stage_tank_share = torch.where(valid_stage_mask, stage_tank_share, torch.zeros_like(stage_tank_share))

        return {
            "score_log": outputs[:, 0],
            "launch_mass_log": outputs[:, 1],
            "boiloff_log": outputs[:, 2],
            "dry_mass_log": aux_outputs[:, 0],
            "tank_mass_log": aux_outputs[:, 1],
            "tank_carry_log": aux_outputs[:, 2],
            "stage_dv_share": stage_dv_share,
            "stage_tank_share": stage_tank_share,
        }
