"""Tests for the internal Pass 1 / verifier schema additions (no .NET contract change)."""

from __future__ import annotations

from google.genai import types

from tests.support import CODE_STEAL
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer, _build_pass1_response_schema
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.schemas.draft import DraftEvent
from video_analyzer.verifier.prompt import build_verifier_response_schema


def test_pass1_response_schema_exposes_acting_team_and_attack_direction() -> None:
    schema = _build_pass1_response_schema(types)

    event_props = schema.properties["period_events"].items.properties
    assert set(event_props["acting_team"].enum) == {"tracked", "opponent", "unknown"}
    assert set(schema.properties["tracked_attack_direction"].enum) == {"left", "right", "unknown"}
    # Both are optional so older prompts and cached responses keep validating.
    assert "acting_team" not in schema.properties["period_events"].items.required
    assert "tracked_attack_direction" not in schema.required


def test_new_pass1_fields_default_to_unknown_when_the_model_omits_them() -> None:
    result = Pass1ChunkResult.model_validate(
        {
            "period_events": [
                {
                    "time_sec": 1.0,
                    "prompt_code": CODE_STEAL,
                    "period_number": 1,
                    "confidence": 0.9,
                }
            ],
            "time_anchors": [],
        }
    )

    assert result.tracked_attack_direction == "unknown"
    assert result.period_events[0].acting_team == "unknown"


def test_pass1_payload_with_the_new_fields_validates() -> None:
    result = GeminiPass1Analyzer._validate_payload_dict(
        {
            "tracked_attack_direction": "left",
            "period_events": [
                {
                    "time_sec": 1.0,
                    "prompt_code": CODE_STEAL,
                    "period_number": 1,
                    "confidence": 0.9,
                    "acting_team": "opponent",
                }
            ],
            "time_anchors": [],
        },
        chunk_index=0,
    )

    assert result.tracked_attack_direction == "left"
    assert result.period_events[0].acting_team == "opponent"


def test_internal_fields_never_leak_into_the_draft_event_contract() -> None:
    fields = set(DraftEvent.model_fields)

    assert "acting_team" not in fields
    assert "tracked_attack_direction" not in fields


def test_verifier_response_schema_restricts_verdicts_and_teams() -> None:
    schema = build_verifier_response_schema(types)

    verdict_props = schema.properties["verdicts"].items.properties
    assert set(verdict_props["verdict"].enum) == {"keep", "drop", "relabel"}
    assert set(verdict_props["acting_team"].enum) == {"tracked", "opponent", "unknown"}
    assert {"item_id", "verdict", "confidence"} <= set(schema.properties["verdicts"].items.required)
