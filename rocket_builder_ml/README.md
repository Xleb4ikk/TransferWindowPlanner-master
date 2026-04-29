# Rocket Builder ML

This module trains a neural ranker on top of `data/datasets/fuel-dataset`.

The ranker learns from `architecture_dataset.csv` and scores candidate rocket architectures for a mission. The newer tank-aware training path explicitly teaches the model about dry mass, tank mass, and upper-stage inert carry penalties in addition to the main score target.

## What It Learns

Inputs:

- mission delta-v, travel time, payload, solar-distance context
- stage propellant / engine layout
- stage-level numeric priors for storage, boiloff, tankage, thrust, and Isp

Outputs:

- best architecture key
- predicted launch mass
- predicted mass-equivalent score
- predicted boiloff mass
- auxiliary dry-mass / tank-mass / tank-carry estimates during training

## Quick Launch

```powershell
.\rocket_builder_ml\run.ps1 train smoke
.\rocket_builder_ml\run.ps1 train big
.\rocket_builder_ml\run.ps1 train outer_planet_big
```

Prediction:

```powershell
.\rocket_builder_ml\run.ps1 predict big -ModelDir artifacts/rocket-builder-ml-tank-aware-big -DvTotalMps 10166.6 -TravelDays 283.1 -SolarDistanceAu 1.337 -MinDistanceAu 1.017 -PayloadMassKg 44470.7 -Origin Earth -Destination Mars
```

Preview the generated command:

```powershell
.\rocket_builder_ml\run.ps1 train base -DryRun
```

Pass extra CLI flags after `--`:

```powershell
.\rocket_builder_ml\run.ps1 train big -ExtraArgs '--batch-size','7168','--epochs','96'
```

## Train

```powershell
python -m rocket_builder_ml.train `
  --dataset-dir data/datasets/fuel-dataset `
  --output-dir artifacts/rocket-builder-ml-tank-aware-big `
  --epochs 72 `
  --batch-size 4096 `
  --hidden-dims 1024,768,512,384,256 `
  --device cuda
```

If CUDA is not available in PyTorch, switch to `--device cpu` or install a CUDA-enabled wheel first.

Helper inspection scripts are in `tools/ml/`.

## Predict

```powershell
python -m rocket_builder_ml.predict `
  --model-dir artifacts/rocket-builder-ml-tank-aware-big `
  --dv-total-mps 10166.6 `
  --travel-days 283.1 `
  --solar-distance-au 1.337 `
  --min-distance-au 1.017 `
  --payload-mass-kg 44470.7 `
  --top-k 5
```

You can also pass the mission as JSON:

```json
{
  "dv_total_mps": 10166.6,
  "travel_days": 283.1,
  "mean_distance_au": 1.337,
  "min_distance_au": 1.017,
  "payload_mass_kg": 44470.7
}
```

## GPU Note

Check whether the installed PyTorch build sees CUDA:

```powershell
@'
import torch
print(torch.__version__)
print(torch.cuda.is_available())
'@ | python -
```

If that prints `False`, install a CUDA-enabled PyTorch wheel and rerun training with `--device cuda`.
