"""Tests for OCR cap-color verification and the strict auto-confirm gates (D3, D6)."""

from __future__ import annotations

import json
from types import SimpleNamespace
from uuid import uuid4

import pytest

from tests.support import make_candidate, make_context
from video_analyzer.pass2.cap_resolver import (
    CapResolutionResult,
    GeminiCropGridCapResolver,
    LocalOcrCapResolver,
    _build_cap_grid_response_schema,
    build_cap_grid_prompt,
)
from video_analyzer.pass2.failure_reasons import (
    REVIEW_ACTOR_TEAM_UNVERIFIED,
    REVIEW_OCR_CAP_COLOR_MISMATCH,
    REVIEW_OPPONENT_GOALKEEPER_SIDE,
    REVIEW_WEAK_ACTOR_MATCH,
)
from video_analyzer.pass2.lineup_matcher import (
    MIN_AUTO_CONFIRM_MATCH_SCORE,
    ActorEvidence,
    LineupMatcher,
    LineupMatchOutcome,
)
from video_analyzer.pass2.team_classifier import TeamClass
from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator
from video_analyzer.sport import DefaultProfile, WaterPoloProfile

MIN_CAP_CONFIDENCE = 0.80
GOOD_SCORE = 0.9


# ------------------------------------------------------------ Gemini schema, prompt, result


class _Models:
    def __init__(self, payload: dict) -> None:
        self.payload = payload
        self.contents: list = []

    def generate_content(self, model, contents, config):
        self.contents = contents
        return SimpleNamespace(text=json.dumps(self.payload), parsed=None, usage_metadata=None)


def _resolver(payload: dict) -> tuple[GeminiCropGridCapResolver, _Models]:
    models = _Models(payload)
    resolver = GeminiCropGridCapResolver(api_key="k", client=SimpleNamespace(models=models))
    return resolver, models


def _crops() -> SimpleNamespace:
    return SimpleNamespace(contact_sheet_jpeg=b"jpeg", crops=[])


def test_response_schema_declares_tile_and_observed_cap_color() -> None:
    from google.genai import types

    schema = _build_cap_grid_response_schema(types)

    assert "observed_cap_color" in schema.properties
    assert schema.properties["observed_cap_color"].nullable is True
    tile_properties = schema.properties["tile_readings"].items.properties
    assert "cap_color" in tile_properties
    assert "observed_cap_color" not in schema.required


def test_prompt_names_expected_and_opponent_colors_and_the_null_on_mismatch_rule() -> None:
    prompt = build_cap_grid_prompt([13, 7], expected_cap_color="white", opponent_cap_color="blue")

    assert "Expected cap color of the acting player: white." in prompt
    assert "Opponent team cap color (must NOT be accepted): blue." in prompt
    assert "`observed_cap_color`" in prompt
    assert "`cap_color`" in prompt
    assert "does NOT match the expected cap color" in prompt
    assert "set `consensus_cap_number` to null" in prompt
    assert "[7, 13]" in prompt


def test_prompt_omits_color_lines_without_hints() -> None:
    prompt = build_cap_grid_prompt([7])

    assert "Expected cap color" not in prompt
    assert "Opponent team cap color" not in prompt


def test_gemini_resolver_sends_both_colors_and_propagates_observed_cap_color() -> None:
    resolver, models = _resolver(
        {
            "tile_readings": [
                {"tile_index": 1, "cap_number": 7, "confidence": 0.9, "cap_color": "white"}
            ],
            "consensus_cap_number": 7,
            "confidence": 0.92,
            "candidate_numbers": [],
            "observed_cap_color": " White ",
            "reasoning": "clear",
        }
    )

    result = resolver.resolve(
        _crops(), [7], cap_color_hint="white", opponent_cap_color="blue", crop_subject=None
    )

    assert "Expected cap color of the acting player: white." in models.contents[-1]
    assert "Opponent team cap color (must NOT be accepted): blue." in models.contents[-1]
    assert result.resolved_cap_number == 7
    assert result.observed_cap_color == "White"
    assert result.raw_response is not None
    assert result.raw_response["tile_readings"][0]["cap_color"] == "white"


def test_gemini_resolver_reports_observed_color_when_the_model_nulls_the_consensus() -> None:
    resolver, _ = _resolver(
        {
            "tile_readings": [],
            "consensus_cap_number": None,
            "confidence": 0.2,
            "candidate_numbers": [],
            "observed_cap_color": "blue",
            "reasoning": "blue cap, expected white",
        }
    )

    result = resolver.resolve(_crops(), [7], cap_color_hint="white", opponent_cap_color="blue")

    assert result.resolved_cap_number is None
    assert result.observed_cap_color == "blue"


def test_gemini_resolver_tolerates_responses_without_color_fields() -> None:
    resolver, _ = _resolver(
        {
            "tile_readings": [],
            "consensus_cap_number": 7,
            "confidence": 0.9,
            "candidate_numbers": [],
            "reasoning": "ok",
        }
    )

    result = resolver.resolve(_crops(), [7])

    assert result.resolved_cap_number == 7
    assert result.observed_cap_color is None


def test_local_ocr_resolver_ignores_colors_and_reports_none() -> None:
    reader = SimpleNamespace(readtext=lambda image, allowlist="": [([], "7", 0.98)])
    crop = SimpleNamespace(crop_bgr=object())
    resolver = LocalOcrCapResolver(reader=reader)

    result = resolver.resolve(
        SimpleNamespace(crops=[crop]), [7], cap_color_hint="white", opponent_cap_color="blue"
    )

    assert result.resolved_cap_number == 7
    assert result.observed_cap_color is None
    assert result.raw_response is None


# ------------------------------------------------------------------- OCR color guard


def _confirmed_outcome(lineup_id=None) -> LineupMatchOutcome:
    return LineupMatchOutcome(
        match_lineup_id=lineup_id or uuid4(),
        resolved_cap_number=7,
        cap_confidence=0.95,
        candidate_lineup_ids=[],
        needs_review=False,
        review_reasons=[],
    )


@pytest.mark.parametrize("observed", ["blue", "Navy", "dark blue", "red"])
def test_observed_cap_color_mismatch_blocks_auto_confirm(observed: str) -> None:
    context = make_context()
    candidate = make_candidate(context, cap_color_hint="white")

    guarded = VideoAnalysisOrchestrator._apply_cap_color_guard(
        _confirmed_outcome(), candidate, "white", observed
    )

    assert guarded.match_lineup_id is None
    assert guarded.resolved_cap_number is None
    assert guarded.needs_review is True
    assert guarded.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]
    assert guarded.gate_outcomes[REVIEW_OCR_CAP_COLOR_MISMATCH] is True
    assert guarded.cap_confidence == 0.95


@pytest.mark.parametrize(("observed", "expected"), [("White", "white"), ("navy", "blue")])
def test_matching_observed_cap_color_keeps_the_confirmed_outcome(observed, expected) -> None:
    context = make_context()
    candidate = make_candidate(context, cap_color_hint=None)
    outcome = _confirmed_outcome()

    guarded = VideoAnalysisOrchestrator._apply_cap_color_guard(
        outcome, candidate, expected, observed
    )

    assert guarded is outcome


@pytest.mark.parametrize(
    ("observed", "expected"), [(None, "white"), ("white", None), ("", "white")]
)
def test_missing_observed_or_expected_color_never_blocks(observed, expected) -> None:
    context = make_context()
    candidate = make_candidate(context, cap_color_hint=None)
    outcome = _confirmed_outcome()

    guarded = VideoAnalysisOrchestrator._apply_cap_color_guard(
        outcome, candidate, expected, observed
    )

    assert guarded is outcome


def test_goalkeeper_event_compares_the_observed_color_with_the_goalkeeper_cap_color() -> None:
    context = make_context()
    candidate = make_candidate(context, "Goal", False, cap_color_hint="red")
    expected = context.profile.expected_actor_cap_color(candidate.definition, "white")

    blocked = VideoAnalysisOrchestrator._apply_cap_color_guard(
        _confirmed_outcome(), candidate, expected, "white"
    )
    allowed = VideoAnalysisOrchestrator._apply_cap_color_guard(
        outcome := _confirmed_outcome(), candidate, expected, "red"
    )

    assert expected == "red"
    assert blocked.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]
    assert allowed is outcome


def test_guard_reports_both_hint_and_ocr_color_mismatches() -> None:
    context = make_context()
    candidate = make_candidate(context, cap_color_hint="blue")

    guarded = VideoAnalysisOrchestrator._apply_cap_color_guard(
        _confirmed_outcome(), candidate, "white", "blue"
    )

    assert guarded.review_reasons == ["CAP_COLOR_HINT_MISMATCH", REVIEW_OCR_CAP_COLOR_MISMATCH]


# -------------------------------------------------------------------- lineup auto-confirm gates


def _resolve(context, evidence, name="Goal", is_positive=True, number=7, **candidate_kwargs):
    candidate = make_candidate(context, name, is_positive, **candidate_kwargs)
    cap_result = CapResolutionResult(
        resolved_cap_number=number, confidence=0.95, candidate_scores={number: 0.95}
    )
    return LineupMatcher().resolve(
        candidate,
        cap_result,
        context,
        MIN_CAP_CONFIDENCE,
        actor_evidence=evidence,
    )


def test_strong_tracked_actor_still_auto_confirms() -> None:
    outcome = _resolve(make_context(), ActorEvidence(GOOD_SCORE, TeamClass.TRACKED))

    assert outcome.match_lineup_id is not None
    assert outcome.review_reasons == []
    assert outcome.gate_outcomes == {
        REVIEW_WEAK_ACTOR_MATCH: False,
        REVIEW_ACTOR_TEAM_UNVERIFIED: False,
        REVIEW_OPPONENT_GOALKEEPER_SIDE: False,
    }


def test_weak_actor_match_blocks_auto_confirm_without_forcing_a_pick() -> None:
    weak = MIN_AUTO_CONFIRM_MATCH_SCORE - 0.01

    outcome = _resolve(make_context(), ActorEvidence(weak, TeamClass.TRACKED))

    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert outcome.review_reasons == [REVIEW_WEAK_ACTOR_MATCH]
    assert [c.cap_number for c in outcome.candidate_lineup_ids] == [7]


def test_match_score_at_the_threshold_is_accepted() -> None:
    outcome = _resolve(
        make_context(), ActorEvidence(MIN_AUTO_CONFIRM_MATCH_SCORE, TeamClass.TRACKED)
    )

    assert outcome.match_lineup_id is not None


@pytest.mark.parametrize("team_class", [TeamClass.UNKNOWN, TeamClass.OPPONENT])
def test_unverified_team_blocks_auto_confirm(team_class: TeamClass) -> None:
    outcome = _resolve(make_context(), ActorEvidence(GOOD_SCORE, team_class))

    assert outcome.match_lineup_id is None
    assert outcome.review_reasons == [REVIEW_ACTOR_TEAM_UNVERIFIED]


def test_goalkeeper_team_class_is_verified() -> None:
    outcome = _resolve(
        make_context(), ActorEvidence(GOOD_SCORE, TeamClass.GOALKEEPER), "Goal", False, number=1
    )

    assert outcome.match_lineup_id is not None


def test_both_actor_gates_report_both_reasons() -> None:
    outcome = _resolve(make_context(), ActorEvidence(0.1, TeamClass.UNKNOWN))

    assert outcome.review_reasons == [REVIEW_WEAK_ACTOR_MATCH, REVIEW_ACTOR_TEAM_UNVERIFIED]


def test_gates_are_skipped_without_actor_evidence_for_backward_compatibility() -> None:
    outcome = _resolve(make_context(), None)

    assert outcome.match_lineup_id is not None
    assert REVIEW_WEAK_ACTOR_MATCH not in outcome.gate_outcomes


# ------------------------------------------------------- D6 goalkeeper-side review hook


def test_goalkeeper_event_on_the_attacked_goal_side_blocks_auto_confirm() -> None:
    outcome = _resolve(
        make_context(),
        ActorEvidence(GOOD_SCORE, TeamClass.GOALKEEPER),
        "Goal",
        False,
        number=1,
        attack_direction="right",
        pool_zone_hint="right_goal_area",
    )

    assert outcome.match_lineup_id is None
    assert outcome.needs_review is True
    assert outcome.review_reasons == [REVIEW_OPPONENT_GOALKEEPER_SIDE]
    assert outcome.gate_outcomes[REVIEW_OPPONENT_GOALKEEPER_SIDE] is True


def test_goalkeeper_event_on_the_defended_side_auto_confirms() -> None:
    outcome = _resolve(
        make_context(),
        ActorEvidence(GOOD_SCORE, TeamClass.GOALKEEPER),
        "Goal",
        False,
        number=1,
        attack_direction="right",
        pool_zone_hint="left_goal_area",
    )

    assert outcome.match_lineup_id is not None


def test_side_check_is_review_only_and_runs_without_actor_evidence() -> None:
    outcome = _resolve(
        make_context(),
        None,
        "Goal",
        False,
        number=1,
        attack_direction="left",
        pool_zone_hint="left_wing_5m",
    )

    assert outcome.match_lineup_id is None
    assert outcome.review_reasons == [REVIEW_OPPONENT_GOALKEEPER_SIDE]


@pytest.mark.parametrize(
    ("direction", "zone"),
    [
        ("unknown", "right_goal_area"),
        ("right", "home_goal"),
        ("right", None),
        ("right", "left_and_right_wing"),
        ("", "right_goal_area"),
    ],
)
def test_water_polo_side_check_ignores_ambiguous_or_unknown_geometry(direction, zone) -> None:
    context = make_context(profile=WaterPoloProfile())
    conceded = make_candidate(context, "Goal", False).definition

    assert WaterPoloProfile().goalkeeper_side_violation(conceded, direction, zone) is False


def test_water_polo_side_check_only_applies_to_goalkeeper_events() -> None:
    context = make_context(profile=WaterPoloProfile())
    field_goal = make_candidate(context, "Goal", True).definition
    conceded = make_candidate(context, "Goal", False).definition
    profile = WaterPoloProfile()

    assert profile.goalkeeper_side_violation(field_goal, "right", "right_wing") is False
    assert profile.goalkeeper_side_violation(conceded, "right", "Right_Goal") is True
    assert profile.goalkeeper_side_violation(conceded, "left", "right_goal") is False


def test_default_profile_side_check_is_a_no_op() -> None:
    context = make_context(profile=DefaultProfile())
    conceded = make_candidate(context, "Goal", False).definition

    assert DefaultProfile().goalkeeper_side_violation(conceded, "right", "right_goal") is False


# ------------------------------------------------ bi-colored goalkeeper prompt wording


def test_goalkeeper_prompt_explains_bi_colored_caps_and_rejects_opponent_bases() -> None:
    prompt = build_cap_grid_prompt(
        [1, 13],
        expected_cap_color="red",
        opponent_cap_color="blue",
        goalkeeper_cap_color="red",
    )

    assert "Goalkeeper caps may be solid red or bi-colored" in prompt
    assert "red panels or ear-guards on a team-color base" in prompt
    assert "report `observed_cap_color` as 'red'" in prompt
    assert "do NOT treat it as a mismatch" in prompt
    assert "A red cap on the opponent's blue base belongs to the opponent goalkeeper" in prompt
    assert "set `consensus_cap_number` to null and report the base color" in prompt


def test_goalkeeper_prompt_without_opponent_color_omits_the_opponent_rule() -> None:
    prompt = build_cap_grid_prompt([1], expected_cap_color="red", goalkeeper_cap_color="red")

    assert "bi-colored" in prompt
    assert "opponent's" not in prompt


def test_field_player_prompt_has_no_goalkeeper_wording() -> None:
    prompt = build_cap_grid_prompt(
        [7], expected_cap_color="white", opponent_cap_color="blue", goalkeeper_cap_color="red"
    )

    assert "bi-colored" not in prompt
    assert "ear-guards" not in prompt
    assert "Expected cap color of the acting player: white." in prompt


def test_goalkeeper_color_match_is_alias_aware() -> None:
    prompt = build_cap_grid_prompt([1], expected_cap_color="Red", goalkeeper_cap_color="red")

    assert "bi-colored" in prompt


def test_gemini_resolver_forwards_the_goalkeeper_cap_color_into_the_prompt() -> None:
    resolver, models = _resolver(
        {
            "tile_readings": [],
            "consensus_cap_number": 1,
            "confidence": 0.9,
            "candidate_numbers": [],
            "observed_cap_color": "red",
            "reasoning": "ok",
        }
    )

    result = resolver.resolve(
        _crops(),
        [1],
        cap_color_hint="red",
        opponent_cap_color="blue",
        goalkeeper_cap_color="red",
    )

    assert "bi-colored" in models.contents[-1]
    assert result.observed_cap_color == "red"


def test_local_ocr_resolver_accepts_the_goalkeeper_cap_color_argument() -> None:
    reader = SimpleNamespace(readtext=lambda image, allowlist="": [([], "7", 0.98)])
    resolver = LocalOcrCapResolver(reader=reader)

    result = resolver.resolve(
        SimpleNamespace(crops=[SimpleNamespace(crop_bgr=object())]),
        [7],
        cap_color_hint="red",
        opponent_cap_color="blue",
        goalkeeper_cap_color="red",
    )

    assert result.resolved_cap_number == 7
    assert result.observed_cap_color is None


def test_goalkeeper_red_observation_is_accepted_for_goalkeeper_events() -> None:
    context = make_context()
    candidate = make_candidate(context, "Goal", False, cap_color_hint="red")
    expected = context.profile.expected_actor_cap_color(candidate.definition, "white")
    outcome = _confirmed_outcome()

    accepted = VideoAnalysisOrchestrator._apply_cap_color_guard(outcome, candidate, expected, "red")
    rejected = VideoAnalysisOrchestrator._apply_cap_color_guard(
        _confirmed_outcome(), candidate, expected, "blue"
    )

    assert accepted is outcome
    assert rejected.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]


# ------------------------------------- per-tile cap colors when the overall color is missing


def _guard(observed, tiles, expected="white", opponent="blue"):
    candidate = make_candidate(make_context(), cap_color_hint=None)
    outcome = _confirmed_outcome()
    guarded = VideoAnalysisOrchestrator._apply_cap_color_guard(
        outcome,
        candidate,
        expected,
        observed,
        tile_cap_colors=tuple(tiles),
        opponent_cap_color=opponent,
    )
    return outcome, guarded


def test_mixed_tiles_without_an_overall_color_trigger_the_ocr_color_mismatch() -> None:
    _, guarded = _guard(None, ["white", "blue", "blue", "white"])

    assert guarded.match_lineup_id is None
    assert guarded.resolved_cap_number is None
    assert guarded.needs_review is True
    assert guarded.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]
    assert guarded.gate_outcomes[REVIEW_OCR_CAP_COLOR_MISMATCH] is True


def test_single_opponent_tile_triggers_when_no_overall_color_was_reported() -> None:
    _, guarded = _guard(None, ["white", "white", "white", "blue"])

    assert guarded.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]


def test_half_opponent_tiles_trigger_even_when_the_overall_color_matches() -> None:
    _, guarded = _guard("white", ["white", "blue", "blue", "white"])

    assert guarded.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]


def test_clean_white_sheet_with_one_noisy_blue_tile_does_not_trigger() -> None:
    outcome, guarded = _guard("white", ["white", "white", "blue", "white"])

    assert guarded is outcome


def test_all_white_tiles_without_an_overall_color_do_not_trigger() -> None:
    outcome, guarded = _guard(None, ["white", "white", None, "white"])

    assert guarded is outcome


def test_tile_colors_are_ignored_without_an_opponent_color_or_tile_readings() -> None:
    outcome, no_opponent = _guard(None, ["blue", "blue"], opponent=None)
    _, no_tiles = _guard(None, [])

    assert no_opponent is outcome
    assert no_tiles.match_lineup_id is not None


def test_null_and_blank_tiles_are_not_counted_for_the_opponent_share() -> None:
    outcome, guarded = _guard("white", ["white", None, "", "white", "blue"])

    assert guarded is outcome  # 1 of 3 reported tiles is blue: a minority


def test_tile_colors_match_the_opponent_through_aliases() -> None:
    _, guarded = _guard(None, ["navy", "white"], opponent="blue")

    assert guarded.review_reasons == [REVIEW_OCR_CAP_COLOR_MISMATCH]


def test_gemini_resolver_propagates_per_tile_cap_colors() -> None:
    resolver, _ = _resolver(
        {
            "tile_readings": [
                {"tile_index": 1, "cap_number": None, "confidence": 0.4, "cap_color": "white"},
                {"tile_index": 2, "cap_number": None, "confidence": 0.3, "cap_color": " Blue "},
                {"tile_index": 3, "cap_number": None, "confidence": 0.3},
            ],
            "consensus_cap_number": None,
            "confidence": 0.2,
            "candidate_numbers": [],
            "observed_cap_color": None,
            "reasoning": "mixed colors",
        }
    )

    result = resolver.resolve(_crops(), [7], cap_color_hint="white", opponent_cap_color="blue")

    assert result.tile_cap_colors == ("white", "Blue", None)
    assert result.observed_cap_color is None


def test_local_ocr_resolver_reports_no_tile_colors() -> None:
    reader = SimpleNamespace(readtext=lambda image, allowlist="": [([], "7", 0.98)])
    resolver = LocalOcrCapResolver(reader=reader)

    result = resolver.resolve(SimpleNamespace(crops=[SimpleNamespace(crop_bgr=object())]), [7])

    assert result.tile_cap_colors == ()
    assert result.observed_cap_color is None
