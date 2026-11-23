"""Configurable mapping from provider-specific class labels to canonical roles."""

from __future__ import annotations

from video_analyzer.detectors.base import CanonicalRole


class ClassMapAdapter:
    """Translates arbitrary model class labels into internal CanonicalRole values."""

    def __init__(self, mapping: dict[str, str]) -> None:
        self._lookup: dict[str, CanonicalRole] = {}
        for raw_label, role_name in mapping.items():
            key = raw_label.strip().lower()
            normalized_role = role_name.strip().lower()
            try:
                self._lookup[key] = CanonicalRole(normalized_role)
            except ValueError as exc:
                valid = [r.value for r in CanonicalRole]
                raise ValueError(
                    f"Invalid canonical role '{role_name}' for class '{raw_label}'. "
                    f"Expected one of {valid}."
                ) from exc

    def map_class(self, raw_class_name: str, fallback_id: int | None = None) -> CanonicalRole:
        """Resolve provider class name (or fallback numeric ID) to CanonicalRole."""
        normalized = raw_class_name.strip().lower()
        if normalized in self._lookup:
            return self._lookup[normalized]
        if fallback_id is not None:
            id_key = str(fallback_id)
            if id_key in self._lookup:
                return self._lookup[id_key]
        return CanonicalRole.UNKNOWN
