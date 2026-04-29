import sys
from pathlib import Path

import torch

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT))

from rocket_builder_ml.model import ArchitectureRanker

# Default configuration from train.py
model = ArchitectureRanker(
    numeric_dim=22,
    propellant_vocab_size=6,
    stage_slots=5,
    engine_vocab_size=50,
    origin_vocab_size=15,
    destination_vocab_size=15,
    hidden_dims=(1024, 768, 512, 384, 256),
    propellant_embedding_dim=40,
    engine_embedding_dim=24,
    stage_count_embedding_dim=20,
    mission_body_embedding_dim=24,
    stage_numeric_dim=0,
    dropout=0.08,
)

# Count total parameters
total_params = sum(p.numel() for p in model.parameters())
trainable_params = sum(p.numel() for p in model.parameters() if p.requires_grad)

print(f"Total parameters: {total_params:,}")
print(f"Trainable parameters: {trainable_params:,}")

print("\nParameter breakdown:")
print(f"  Propellant embedding: {model.propellant_embedding.weight.numel():,}")
print(f"  Engine embedding: {model.engine_embedding.weight.numel() if model.engine_embedding else 0:,}")
print(f"  Backbone MLP: {sum(p.numel() for p in model.backbone.parameters()):,}")
print(f"  Output heads: {sum(p.numel() for p in model.output_head.parameters()) + sum(p.numel() for p in model.aux_output_head.parameters()):,}")
