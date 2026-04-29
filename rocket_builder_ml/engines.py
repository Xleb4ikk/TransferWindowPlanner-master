from __future__ import annotations

from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
import json

NONE_ENGINE_KEY = "none"


@dataclass(frozen=True)
class EngineSpec:
    key: str
    display_name: str
    propellant_key: str
    vacuum_isp_seconds: float
    vacuum_thrust_kn: float
    dry_mass_kg: float
    max_cluster_count: int
    selection_rank: int
    supports_restart: bool
    supports_long_coast: bool
    supported_roles: tuple[str, ...]
    supported_segments: tuple[str, ...]

    @classmethod
    def from_dict(cls, payload: dict[str, object]) -> "EngineSpec":
        return cls(
            key=str(payload["key"]),
            display_name=str(payload["displayName"]),
            propellant_key=str(payload["propellantKey"]),
            vacuum_isp_seconds=float(payload["vacuumIspSeconds"]),
            vacuum_thrust_kn=float(payload["vacuumThrustkN"]),
            dry_mass_kg=float(payload["dryMassKg"]),
            max_cluster_count=int(payload["maxClusterCount"]),
            selection_rank=int(payload.get("selectionRank", 100)),
            supports_restart=bool(payload.get("supportsRestart", False)),
            supports_long_coast=bool(payload.get("supportsLongCoast", False)),
            supported_roles=tuple(str(value) for value in payload.get("supportedRoles", [])),
            supported_segments=tuple(str(value) for value in payload.get("supportedSegments", [])),
        )

    def supports_role(self, role: str) -> bool:
        return role in self.supported_roles

    def supports_segment(self, segment: str) -> bool:
        return not self.supported_segments or segment in self.supported_segments


@lru_cache(maxsize=1)
def load_engine_catalog() -> tuple[EngineSpec, ...]:
    path = Path(__file__).resolve().parent.parent / "StandaloneTrajectoryCalculator" / "engine_catalog.json"
    payload = json.loads(path.read_text(encoding="utf-8"))
    engines = tuple(EngineSpec.from_dict(item) for item in payload)
    if not engines:
        raise ValueError(f"Engine catalog is empty: {path}")
    return engines


def engine_display_map() -> dict[str, str]:
    return {engine.key: engine.display_name for engine in load_engine_catalog()}


def select_engine_for_stage(propellant_key: str, role: str, segment: str) -> EngineSpec | None:
    if propellant_key in {"none", ""}:
        return None

    engines = load_engine_catalog()
    exact_matches = [
        engine
        for engine in engines
        if engine.propellant_key == propellant_key
        and engine.supports_role(role)
        and engine.supports_segment(segment)
    ]
    if exact_matches:
        return sorted(exact_matches, key=lambda item: (item.selection_rank, -item.vacuum_isp_seconds, item.dry_mass_kg))[0]

    segment_matches = [
        engine
        for engine in engines
        if engine.propellant_key == propellant_key
        and engine.supports_segment(segment)
    ]
    if segment_matches:
        return sorted(segment_matches, key=lambda item: (item.selection_rank, -item.vacuum_isp_seconds, item.dry_mass_kg))[0]

    propellant_matches = [engine for engine in engines if engine.propellant_key == propellant_key]
    if propellant_matches:
        return sorted(propellant_matches, key=lambda item: (item.selection_rank, -item.vacuum_isp_seconds, item.dry_mass_kg))[0]

    return None
