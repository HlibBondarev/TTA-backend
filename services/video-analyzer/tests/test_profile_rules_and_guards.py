"""Tests for goalkeeper profile logic, Pass 1 prompt rules, dedup filters and the lineup guard."""

from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

from tests.support import (
    CODE_GOAL,
    CODE_GOAL_CONCEDED,
    CODE_STEAL,
    load_sample_snapshot,
    make_context,
    prompt_code_for,
)
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.deduplicator import EventDeduplicator
from video_analyzer.pass1.models import (
    Pass1ChunkResult,
    Pass1EventCandidate,
    Pass1RawEvent,
)
from video_analyzer.pass1.prompt_builder import Pass1PromptBuilder
from video_analyzer.pass2.cap_resolver import CapResolutionResult, GeminiCropGridCapResolver
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.sport import (
    AnalysisContext,
    DefaultProfile,
    WaterPoloProfile,
    definition_key,
)


def _definition(context: AnalysisContext, name: str, positive: bool = True):
    return next(d for d in context.event_catalog if d.name == name and d.is_positive == positive)


def _candidate(context: AnalysisContext, name: str, positive: bool = True) -> Pass1EventCandidate:
    return Pass1EventCandidate(
        video_time_sec=55.0,
        definition=_definition(context, name, positive),
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=[0.4, 0.4, 0.5, 0.6],
        pool_zone_hint=None,
        cap_color_hint=None,
        confidence=0.92,
    )


# ---------------------------------------------------------------------- profile behavior


def test_water_polo_goalkeeper_events_use_name_and_polarity_pairs(
    sample_context: AnalysisContext,
) -> None:
    profile = WaterPoloProfile()

    assert profile.goalkeeper_cap_color == "red"
    assert ("goal", False) in profile.goalkeeper_event_keys
    assert ("save", True) in profile.goalkeeper_event_keys
    assert ("goalkeeper error", False) in profile.goalkeeper_event_keys
    assert profile.is_goalkeeper_event(_definition(sample_context, "Goal", positive=False))
    assert profile.is_goalkeeper_event(_definition(sample_context, "Save"))
    assert profile.is_goalkeeper_event(_definition(sample_context, "Goalkeeper Error", False))
    # The positive Goal is scored by a field player.
    assert not profile.is_goalkeeper_event(_definition(sample_context, "Goal", positive=True))
    assert not profile.is_goalkeeper_event(_definition(sample_context, "Steal"))


def test_expected_actor_cap_color_is_red_for_goalkeeper_events_only(
    sample_context: AnalysisContext,
) -> None:
    profile = WaterPoloProfile()
    conceded = _definition(sample_context, "Goal", positive=False)
    steal = _definition(sample_context, "Steal")

    assert profile.expected_actor_cap_color(conceded, "white") == "red"
    assert profile.expected_actor_cap_color(steal, "white") == "white"
    assert profile.expected_actor_cap_color(steal, None) is None
    assert DefaultProfile().expected_actor_cap_color(conceded, "white") == "white"


def test_confusable_sets_cover_the_tta_disambiguation_pairs(
    sample_context: AnalysisContext,
) -> None:
    profile = WaterPoloProfile()
    sog = _definition(sample_context, "Shot on Goal")
    miss = _definition(sample_context, "Missed Shot", positive=False)
    steal = _definition(sample_context, "Steal")
    save = _definition(sample_context, "Save")
    goal_pos = _definition(sample_context, "Goal")
    goal_neg = _definition(sample_context, "Goal", positive=False)

    assert profile.are_confusable(sog, miss)
    assert profile.are_confusable(steal, save)
    assert profile.are_confusable(goal_pos, goal_neg)
    assert not profile.are_confusable(sog, steal)
    assert not profile.are_confusable(sog, sog)
    assert DefaultProfile().are_confusable(sog, miss) is False
    assert definition_key(goal_neg) == ("goal", False)


def test_conceded_events_are_the_negative_goal_only(sample_context: AnalysisContext) -> None:
    profile = WaterPoloProfile()

    assert profile.is_conceded_event(_definition(sample_context, "Goal", positive=False))
    assert not profile.is_conceded_event(_definition(sample_context, "Goal"))
    assert not profile.is_conceded_event(_definition(sample_context, "Own Goal", False))


# ---------------------------------------------------------------------- Pass 1 prompt rules


def test_water_polo_prompt_contains_structured_rule_blocks(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c.mp4")

    prompt = Pass1PromptBuilder().build_chunk_prompt(sample_context, chunk)

    for block in WaterPoloProfile().pass1_rules:
        assert f"### {block.title}" in prompt
    assert "tracked_attack_direction" in prompt
    assert "Goalkeepers of BOTH teams wear RED caps" in prompt
    assert "NOT a `Save`" in prompt
    # Polarity example is generated from the catalog prompt codes, not hardcoded names.
    assert f"`{CODE_GOAL}` (Goal, POSITIVE) vs `{CODE_GOAL_CONCEDED}` (Goal, NEGATIVE)" in prompt
    assert "Goal Conceded" not in prompt


def test_default_profile_prompt_has_no_sport_specific_rule_blocks(tmp_path: Path) -> None:
    context = make_context(profile=DefaultProfile())
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c.mp4")

    prompt = Pass1PromptBuilder().build_chunk_prompt(context, chunk)

    assert "Goalkeeper Red Cap Rule" not in prompt
    assert "Pool Orientation" not in prompt


def test_polarity_example_is_omitted_without_same_name_opposite_polarity(
    tmp_path: Path,
) -> None:
    snapshot = load_sample_snapshot()
    unique = [d for d in snapshot.event_definitions if d.name != "Goal"]
    context = make_context(snapshot.model_copy(update={"event_definitions": unique}))
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c.mp4")

    prompt = Pass1PromptBuilder().build_chunk_prompt(context, chunk)

    assert "for example" not in prompt


# ---------------------------------------------------------------------- Pass 2 prompt wording


class _CapturingModels:
    def __init__(self) -> None:
        self.contents: list = []

    def generate_content(self, model, contents, config):
        self.contents = contents
        return SimpleNamespace(
            text=(
                '{"tile_readings": [], "consensus_cap_number": 7, "confidence": 0.9, '
                '"candidate_numbers": [], "reasoning": "ok"}'
            ),
            parsed=None,
            usage_metadata=None,
        )


def _crops() -> SimpleNamespace:
    return SimpleNamespace(contact_sheet_jpeg=b"jpeg", crops=[])


def test_cap_resolver_prompt_uses_profile_supplied_subject_and_occlusion_hint() -> None:
    models = _CapturingModels()
    resolver = GeminiCropGridCapResolver(
        api_key="k", client=SimpleNamespace(models=models), model_name="gemini-3.5-flash-lite"
    )
    profile = WaterPoloProfile()

    resolver.resolve(
        _crops(),
        [7],
        cap_color_hint="red",
        crop_subject=profile.pass2_crop_subject,
        occlusion_hint=profile.pass2_occlusion_hint,
    )

    prompt = models.contents[-1]
    assert "cropped Water Polo player head/cap frames" in prompt
    assert "water splash, blur, or angle" in prompt


def test_cap_resolver_prompt_is_sport_neutral_by_default() -> None:
    models = _CapturingModels()
    resolver = GeminiCropGridCapResolver(
        api_key="k", client=SimpleNamespace(models=models), model_name="gemini-3.5-flash-lite"
    )

    resolver.resolve(_crops(), [7])

    prompt = models.contents[-1]
    assert "Water Polo" not in prompt
    assert "splash" not in prompt
    assert "cropped player head/cap frames" in prompt


# ---------------------------------------------------------------------- dedup filters


def _chunk(tmp_path: Path, index: int, start: float, end: float) -> VideoChunk:
    return VideoChunk(index=index, start_sec=start, end_sec=end, path=tmp_path / f"c{index}.mp4")


def _raw(code: str, time_sec: float, acting_team: str = "tracked") -> Pass1RawEvent:
    return Pass1RawEvent(
        time_sec=time_sec,
        prompt_code=code,
        period_number=1,
        confidence=0.9,
        acting_team=acting_team,
    )


def test_deduplicator_drops_opponent_events_except_conceded_goals(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    chunk = _chunk(tmp_path, 0, 0.0, 120.0)
    result = Pass1ChunkResult(
        period_events=[
            _raw(CODE_STEAL, 10.0, "opponent"),
            _raw(CODE_STEAL, 20.0, "tracked"),
            _raw(CODE_STEAL, 30.0, "unknown"),
            _raw(CODE_GOAL_CONCEDED, 40.0, "opponent"),
        ]
    )

    events, raw_count, warnings = EventDeduplicator().process(
        [(chunk, result)],
        sample_context.event_catalog,
        120.0,
        0.5,
        profile=sample_context.profile,
    )

    assert raw_count == 4
    assert [e.video_time_sec for e in events] == [20.0, 30.0, 40.0]
    assert [e.acting_team for e in events] == ["tracked", "unknown", "opponent"]
    assert any("opponent-team event" in w for w in warnings)


def test_deduplicator_reports_cross_chunk_label_conflicts_without_merging(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    sog = prompt_code_for("Shot on Goal")
    miss = prompt_code_for("Missed Shot", positive=False)
    chunk0 = _chunk(tmp_path, 0, 0.0, 120.0)
    chunk1 = _chunk(tmp_path, 1, 105.0, 225.0)
    # Both chunks see the same moment at ~112 s but label it differently.
    result0 = Pass1ChunkResult(period_events=[_raw(sog, 112.0)])
    result1 = Pass1ChunkResult(period_events=[_raw(miss, 7.5)])

    detailed = EventDeduplicator().process_detailed(
        [(chunk0, result0), (chunk1, result1)],
        sample_context.event_catalog,
        240.0,
        0.5,
        profile=sample_context.profile,
    )

    assert len(detailed.candidates) == 2
    assert len(detailed.conflict_groups) == 1
    labels = {c.definition.prompt_code for c in detailed.conflict_groups[0].candidates}
    assert labels == {sog, miss}


def test_deduplicator_reports_no_conflict_for_same_chunk_or_distant_events(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    sog = prompt_code_for("Shot on Goal")
    miss = prompt_code_for("Missed Shot", positive=False)
    chunk0 = _chunk(tmp_path, 0, 0.0, 120.0)
    chunk1 = _chunk(tmp_path, 1, 105.0, 225.0)
    same_chunk = Pass1ChunkResult(period_events=[_raw(sog, 50.0), _raw(miss, 51.0)])
    far_apart = Pass1ChunkResult(period_events=[_raw(miss, 60.0)])  # global 165 s

    detailed = EventDeduplicator().process_detailed(
        [(chunk0, same_chunk), (chunk1, far_apart)],
        sample_context.event_catalog,
        240.0,
        0.5,
        profile=sample_context.profile,
    )

    assert detailed.conflict_groups == []


# ---------------------------------------------------------------------- lineup guard


def test_goalkeeper_event_cannot_auto_confirm_a_field_player(
    sample_context: AnalysisContext,
) -> None:
    candidate = _candidate(sample_context, "Save")
    cap = CapResolutionResult(resolved_cap_number=7, confidence=0.95, candidate_scores={7: 0.95})

    outcome = LineupMatcher().resolve(candidate, cap, sample_context, min_cap_confidence=0.8)

    assert outcome.match_lineup_id is None
    assert outcome.needs_review is True
    assert "GOALKEEPER_EVENT_NON_GOALKEEPER_ACTOR" in outcome.review_reasons
    assert outcome.candidate_lineup_ids[0].cap_number == 7


def test_goalkeeper_event_confirms_the_goalkeeper_lineup_entry(
    sample_context: AnalysisContext,
) -> None:
    candidate = _candidate(sample_context, "Goal", positive=False)
    cap = CapResolutionResult(resolved_cap_number=1, confidence=0.95, candidate_scores={1: 0.95})

    outcome = LineupMatcher().resolve(candidate, cap, sample_context, min_cap_confidence=0.8)

    goalkeeper = next(e for e in sample_context.lineup if e.number == 1)
    assert outcome.match_lineup_id == goalkeeper.id
    assert outcome.needs_review is False


def test_field_events_and_neutral_profile_are_not_restricted_by_the_goalkeeper_guard(
    sample_context: AnalysisContext,
) -> None:
    field_event = _candidate(sample_context, "Steal")
    cap = CapResolutionResult(resolved_cap_number=7, confidence=0.95, candidate_scores={7: 0.95})

    field_outcome = LineupMatcher().resolve(field_event, cap, sample_context, 0.8)

    neutral_context = make_context(profile=DefaultProfile())
    save = _candidate(neutral_context, "Save")
    neutral_outcome = LineupMatcher().resolve(save, cap, neutral_context, 0.8)

    assert field_outcome.match_lineup_id is not None
    assert neutral_outcome.match_lineup_id is not None
