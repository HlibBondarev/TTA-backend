"""Contract tests for the .NET ``analysis-snapshot`` aligned MatchSnapshot and its boundaries."""

from __future__ import annotations

import json
from pathlib import Path
from unittest.mock import MagicMock
from uuid import UUID, uuid4

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from tests.support import SAMPLE_SNAPSHOT_PATH, make_context
from tests.test_orchestrator_and_api import _build_mock_orchestrator
from video_analyzer.api.app import create_app
from video_analyzer.config import Settings
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.prompt_builder import Pass1PromptBuilder
from video_analyzer.schemas.draft import FoulImpact
from video_analyzer.schemas.request import AnalyzeJobRequest, MatchSnapshot, RuntimeHints

SVG_MARKER = "UNIQUE-PLAYGROUND-SVG-MARKER"
SHOT_MAP_MARKER = "UNIQUE-SHOTMAP-SVG-MARKER"
EXPECTED_LINEUP_SIZE = 15


def _raw_sample() -> dict:
    return json.loads(SAMPLE_SNAPSHOT_PATH.read_text(encoding="utf-8"))


def test_sample_fixture_matches_net_contract_shape() -> None:
    raw = _raw_sample()
    assert set(raw) == {
        "match",
        "teams",
        "sport",
        "sportConfiguration",
        "lineup",
        "eventDefinitions",
    }
    assert set(raw["teams"]) == {"home", "guest", "tracked", "opponent"}
    # The .NET contract carries no legacy fields.
    assert "promptCode" not in raw["eventDefinitions"][0]
    assert "isGoalkeeper" not in raw["lineup"][0]
    assert "capColorHint" not in raw["teams"]["tracked"]


def test_sample_snapshot_parses_with_fifteen_player_tracked_lineup(
    sample_snapshot: MatchSnapshot,
) -> None:
    config = sample_snapshot.sport_configuration
    assert len(sample_snapshot.lineup) == EXPECTED_LINEUP_SIZE
    assert config.lineup_limit == EXPECTED_LINEUP_SIZE
    assert config.roster_limit == EXPECTED_LINEUP_SIZE
    assert len({entry.id for entry in sample_snapshot.lineup}) == EXPECTED_LINEUP_SIZE
    assert len({entry.number for entry in sample_snapshot.lineup}) == EXPECTED_LINEUP_SIZE
    first = sample_snapshot.lineup[0]
    assert (first.position_name, first.position_short_name) == ("Goalkeeper", "GK")
    assert sample_snapshot.teams.tracked.id == sample_snapshot.teams.home.id
    assert sample_snapshot.teams.opponent.id == sample_snapshot.teams.guest.id


def test_playground_and_shot_map_are_preserved_on_the_model(
    sample_snapshot: MatchSnapshot,
) -> None:
    config = sample_snapshot.sport_configuration
    assert config.playground.startswith("<svg")
    assert config.shot_map is not None
    assert config.shot_map.startswith("<svg")


def test_numeric_foul_impact_from_net_is_mapped_to_enum() -> None:
    raw = _raw_sample()
    by_name = {item["name"]: item for item in raw["eventDefinitions"]}
    assert by_name["Red Card (18s)"]["foulImpact"] == 2
    parsed = MatchSnapshot.model_validate(raw)
    impacts = {d.name: d.foul_impact for d in parsed.event_definitions}
    assert impacts["Red Card (18s)"] == FoulImpact.DISQUALIFICATION_WITH_SUB
    assert impacts["Exclusion"] == FoulImpact.PERSONAL_FOUL
    assert impacts["Steal"] == FoulImpact.NONE


def test_event_definitions_require_unique_ids() -> None:
    raw = _raw_sample()
    raw["eventDefinitions"][1]["id"] = raw["eventDefinitions"][0]["id"]
    with pytest.raises(ValidationError, match="unique id"):
        MatchSnapshot.model_validate(raw)


def test_snapshot_with_empty_lineup_still_parses() -> None:
    raw = _raw_sample()
    raw["lineup"] = []
    snapshot = MatchSnapshot.model_validate(raw)
    assert snapshot.lineup == []


def test_job_request_rejects_empty_lineup_and_mismatched_tracked_team(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    payload = sample_job_request.model_dump(mode="json", by_alias=True)
    payload["matchSnapshot"]["lineup"] = []
    with pytest.raises(ValidationError, match="lineup must contain at least one entry"):
        AnalyzeJobRequest.model_validate(payload)

    mismatched = {
        **sample_job_request.model_dump(mode="json", by_alias=True),
        "trackedTeamId": str(uuid4()),
    }
    with pytest.raises(ValidationError, match=r"teams\.tracked\.id"):
        AnalyzeJobRequest.model_validate(mismatched)


def test_api_returns_422_for_empty_lineup_without_starting_a_job(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    settings = Settings(_env_file=None, internal_api_key="secret-test-key")
    orchestrator = MagicMock()
    app = create_app(settings=settings, orchestrator=orchestrator)
    body = sample_job_request.model_dump(mode="json", by_alias=True)
    body["matchSnapshot"]["lineup"] = []

    with TestClient(app) as client:
        response = client.post(
            "/v1/analysis/jobs", json=body, headers={"X-Internal-Api-Key": "secret-test-key"}
        )

    assert response.status_code == 422
    assert "lineup" in response.text
    orchestrator.run.assert_not_called()


def test_draft_match_lineup_id_comes_from_snapshot_lineup_id(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    draft = _build_mock_orchestrator(work_dir=tmp_path).run(sample_job_request)
    lineup_ids = {entry.id for entry in sample_job_request.match_snapshot.lineup}
    resolved = [event for event in draft.events if event.match_lineup_id is not None]
    assert resolved, "the mock pipeline resolves at least one event"

    expected = next(e.id for e in sample_job_request.match_snapshot.lineup if e.number == 7)
    assert resolved[0].match_lineup_id == expected
    for event in draft.events:
        assert {c.match_lineup_id for c in event.candidate_lineup_ids} <= lineup_ids

    serialized = draft.model_dump(mode="json", by_alias=True)["events"][0]
    assert UUID(serialized["matchLineupId"]) == expected


def _chunk(tmp_path: Path) -> VideoChunk:
    return VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c.mp4")


def test_prompt_never_contains_raw_svg_but_uses_runtime_hints(
    sample_snapshot: MatchSnapshot, tmp_path: Path
) -> None:
    config = sample_snapshot.sport_configuration.model_copy(
        update={
            "playground": f'<svg xmlns="x"><title>{SVG_MARKER}</title></svg>',
            "shot_map": f'<svg xmlns="x"><title>{SHOT_MAP_MARKER}</title></svg>',
        }
    )
    snapshot = sample_snapshot.model_copy(update={"sport_configuration": config})
    context = make_context(
        snapshot,
        RuntimeHints(tracked_cap_color_hint="white", opponent_cap_color_hint="dark blue"),
    )

    prompt = Pass1PromptBuilder().build_chunk_prompt(context, _chunk(tmp_path))

    for forbidden in (SVG_MARKER, SHOT_MAP_MARKER, "<svg", "xmlns"):
        assert forbidden not in prompt
    assert "Tracked Team (Home): Home Squad (cap color hint: white)" in prompt
    assert "Opponent Team (Guest): Opponent Squad (cap color hint: dark blue)" in prompt


def test_prompt_defaults_cap_hints_to_unknown_without_runtime_hints(
    sample_snapshot: MatchSnapshot, tmp_path: Path
) -> None:
    context = make_context(sample_snapshot, RuntimeHints())
    prompt = Pass1PromptBuilder().build_chunk_prompt(context, _chunk(tmp_path))
    assert "(cap color hint: unknown)" in prompt
    assert "capColorHint" not in json.dumps(_raw_sample())


def test_water_polo_prompt_keeps_baseline_wording(
    sample_snapshot: MatchSnapshot, tmp_path: Path
) -> None:
    prompt = Pass1PromptBuilder().build_chunk_prompt(
        make_context(sample_snapshot), _chunk(tmp_path)
    )
    for baseline in (
        "(plus negative events conceded by the tracked team, such as a conceded goal) "
        "and match timeline anchors.",
        "- Active players per team in water: 7",
        "audio cues (referee whistles, period horn/buzzer, goal whistle, shot-clock horn) "
        "to pinpoint timestamps.",
        "`PeriodStart` (sprint swim-off whistle), `PeriodEnd` (end-of-period buzzer/horn), "
        "`StoppageStart` (timeout/goal/review pause), and `StoppageEnd` (play restart whistle).",
        "| polarity=POSITIVE | isShot=true | score=1 | foulImpact=None",
        "| foulImpact=PersonalFoul (18s)",
    ):
        assert baseline in prompt


def test_parses_system_text_json_style_payload_from_net() -> None:
    """Mirror System.Text.Json output: camelCase, int enums, offset-less dates, explicit nulls."""
    raw = _raw_sample()
    raw["match"]["scheduledAt"] = "2026-11-23T15:00:00"  # DateTime with Unspecified kind
    raw["match"].update({"matchNumber": None, "venue": None, "temperature": None})
    raw["sportConfiguration"].update({"fieldLength": 25, "fieldWidth": 20, "shotMap": None})
    raw["sportConfiguration"]["maxPersonalFouls"] = None
    for definition in raw["eventDefinitions"]:
        assert isinstance(definition["foulImpact"], int)  # .NET DTO field is `int`, not an enum

    snapshot = MatchSnapshot.model_validate(raw)

    assert snapshot.sport_configuration.shot_map is None
    assert snapshot.sport_configuration.field_length == 25.0
    assert snapshot.match.scheduled_at.year == 2026
    assert snapshot.match.venue is None
