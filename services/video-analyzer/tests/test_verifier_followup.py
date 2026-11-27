"""Follow-up tests: verifier thinking cap, bbox preservation and goal-polarity context."""

from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace
from typing import Any

from google.genai import types

from tests.support import CODE_GOAL, CODE_GOAL_CONCEDED, prompt_code_for
from tests.test_verifier import (
    FakeFFmpeg,
    FakeModels,
    _event,
    _keep_all,
    _run,
    _verdict,
    _verifier,
)
from video_analyzer.config import Settings
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.deduplicator import EventDeduplicator
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawEvent
from video_analyzer.pass1.prompt_builder import Pass1PromptBuilder
from video_analyzer.pipeline.factory import build_verifier
from video_analyzer.sport import AnalysisContext, WaterPoloProfile
from video_analyzer.verifier import GeminiClipVerifier


class _ConfigCapturingModels(FakeModels):
    """FakeModels that also records the ``GenerateContentConfig`` of every request."""

    def __init__(self) -> None:
        super().__init__(_keep_all)
        self.configs: list[Any] = []

    def generate_content(self, model, contents, config):
        self.configs.append(config)
        return super().generate_content(model, contents, config)


def _capturing_verifier(thinking_level: str | None) -> tuple[GeminiClipVerifier, Any]:
    models = _ConfigCapturingModels()
    verifier = GeminiClipVerifier(
        api_key="k",
        ffmpeg_client=FakeFFmpeg(),  # type: ignore[arg-type]
        client=SimpleNamespace(models=models),
        thinking_level=thinking_level,
        batch_size=3,
        max_concurrency=1,
    )
    return verifier, models


# ------------------------------------------------------------------ 1. thinking cap


def test_thinking_config_is_sent_on_every_verifier_call(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models = _capturing_verifier("LOW")
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(7)]

    _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(models.configs) == 3
    for config in models.configs:
        assert config.thinking_config is not None
        assert config.thinking_config.thinking_level == types.ThinkingLevel.LOW
        assert config.automatic_function_calling.disable is True


def test_unset_thinking_level_omits_thinking_config_only_as_an_explicit_opt_out(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models = _capturing_verifier(None)

    _run(verifier, sample_context, [_event(sample_context, "Steal", 10.0)], tmp_path=tmp_path)

    assert models.configs[0].thinking_config is None


def test_default_settings_cap_the_verifier_thinking_level_and_the_factory_wires_it() -> None:
    settings = Settings(_env_file=None, internal_api_key="k", gemini_api_key="g")

    verifier = build_verifier(settings, FakeFFmpeg())  # type: ignore[arg-type]

    assert settings.gemini_verifier_thinking_level == "LOW"
    assert isinstance(verifier, GeminiClipVerifier)
    assert verifier._thinking_level == "LOW"  # noqa: SLF001


# ------------------------------------------------------------------ 2. bbox preservation


def test_actor_bbox_survives_keep_and_relabel_verdicts(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    miss_code = prompt_code_for("Missed Shot", positive=False)
    bbox = [0.2, 0.3, 0.3, 0.5]

    def responder(ids, contents):
        return [
            _verdict(1, confidence=0.95),
            _verdict(2, verdict="relabel", relabel_prompt_code=miss_code),
        ]

    verifier, _, _ = _verifier(responder)
    kept = _event(sample_context, "Steal", 20.0)
    relabeled = _event(sample_context, "Shot on Goal", 40.0)
    kept.actor_bbox_normalized = list(bbox)
    relabeled.actor_bbox_normalized = list(bbox)

    result = _run(verifier, sample_context, [kept, relabeled], tmp_path=tmp_path)

    assert [e.actor_bbox_normalized for e in result.events] == [bbox, bbox]
    assert result.events[1].definition.name == "Missed Shot"


def test_actor_bbox_survives_the_fail_open_path(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, _, _ = _verifier(lambda ids, contents: [])
    event = _event(sample_context, "Steal", 20.0)
    event.actor_bbox_normalized = [0.1, 0.1, 0.2, 0.2]

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    assert result.events[0].actor_bbox_normalized == [0.1, 0.1, 0.2, 0.2]
    assert "UNVERIFIED" in result.events[0].review_reasons


def _chunk(tmp_path: Path, index: int, start: float, end: float) -> VideoChunk:
    return VideoChunk(index=index, start_sec=start, end_sec=end, path=tmp_path / f"c{index}.mp4")


def _raw(time_sec: float, confidence: float, bbox: list[float] | None) -> Pass1RawEvent:
    return Pass1RawEvent(
        time_sec=time_sec,
        prompt_code=CODE_GOAL,
        period_number=1,
        confidence=confidence,
        actor_bbox_normalized=bbox,
    )


def test_deduplicator_keeps_the_winning_events_bbox_when_merging_chunks(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    bbox = [0.4, 0.4, 0.5, 0.6]
    first = Pass1ChunkResult(period_events=[_raw(110.0, 0.8, bbox)])
    second = Pass1ChunkResult(period_events=[_raw(5.0, 0.95, bbox)])  # global 110.0

    events, _, _ = EventDeduplicator().process(
        [(_chunk(tmp_path, 0, 0.0, 120.0), first), (_chunk(tmp_path, 1, 105.0, 225.0), second)],
        sample_context.event_catalog,
        240.0,
        0.5,
    )

    assert len(events) == 1
    assert events[0].actor_bbox_normalized == bbox


# ------------------------------------------------------------------ 3. goal polarity context


def test_chunk_attack_direction_is_carried_onto_candidates_and_survives_merging(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    first = Pass1ChunkResult(
        tracked_attack_direction="left", period_events=[_raw(110.0, 0.8, None)]
    )
    second = Pass1ChunkResult(period_events=[_raw(5.0, 0.95, None)])  # direction unknown

    events, _, _ = EventDeduplicator().process(
        [(_chunk(tmp_path, 0, 0.0, 120.0), first), (_chunk(tmp_path, 1, 105.0, 225.0), second)],
        sample_context.event_catalog,
        240.0,
        0.5,
    )

    # The higher-confidence candidate wins but inherits the known direction.
    assert events[0].tracked_attack_direction == "left"


def test_attack_direction_and_polarity_rules_reach_the_verifier_prompt(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models, _ = _verifier()
    left = _event(sample_context, "Goal", 208.5)
    left.tracked_attack_direction = "left"
    unknown = _event(sample_context, "Steal", 100.0)

    _run(verifier, sample_context, [left, unknown], tmp_path=tmp_path)

    request = models.requests[0]
    headers = [c for c in request if isinstance(c, str) and c.startswith("Clip ")]
    assert "attacks the LEFT goal" in headers[0]
    assert "defends the RIGHT goal" in headers[0]
    assert "attacks the" not in headers[1]  # unknown direction adds no claim
    prompt = request[-1]
    assert "### Goal Polarity Check" in prompt
    assert "DEFENDING goal" in prompt
    assert "MUST be the NEGATIVE `Goal`" in prompt
    assert "MUST be the POSITIVE `Goal`" in prompt
    assert "the tracked team restarting from the center means a conceded (NEGATIVE)" in prompt
    assert "the opponent restarting from the center means the tracked team scored" in prompt


def test_positive_goal_is_relabeled_to_the_conceded_goal_and_keeps_the_opponent_actor(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    def responder(ids, contents):
        return [
            _verdict(
                1,
                verdict="relabel",
                relabel_prompt_code=CODE_GOAL_CONCEDED,
                acting_team="opponent",
                actor_cap_color="red",
            )
        ]

    verifier, _, _ = _verifier(responder)
    wrong = _event(sample_context, "Goal", 208.5)
    wrong.actor_bbox_normalized = [0.1, 0.42, 0.16, 0.5]

    result = _run(verifier, sample_context, [wrong], tmp_path=tmp_path)

    assert len(result.events) == 1
    assert result.events[0].definition.is_positive is False
    assert result.events[0].definition.prompt_code == CODE_GOAL_CONCEDED
    assert result.events[0].acting_team == "opponent"
    # Goal POS -> Goal NEG changes the actor (scorer -> tracked goalkeeper): the old box is stale.
    assert result.events[0].actor_bbox_normalized is None
    assert result.events[0].cap_color_hint == "red"
    assert result.events[0].needs_review is False


def test_conceded_goal_is_relabeled_to_the_positive_goal_within_the_confusable_set(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    def responder(ids, contents):
        return [_verdict(1, verdict="relabel", relabel_prompt_code=CODE_GOAL)]

    verifier, _, _ = _verifier(responder)
    conceded = _event(sample_context, "Goal", 60.0, positive=False)

    result = _run(verifier, sample_context, [conceded], tmp_path=tmp_path)

    assert result.events[0].definition.prompt_code == CODE_GOAL


def test_pass1_prompt_requires_bboxes_and_states_the_goal_polarity_rule(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    chunk = _chunk(tmp_path, 0, 0.0, 120.0)

    prompt = Pass1PromptBuilder().build_chunk_prompt(sample_context, chunk)

    assert "`actor_bbox_normalized` is REQUIRED" in prompt
    assert "not pixels, not 0-1000" in prompt
    assert "goal the ball ENTERED" in prompt
    assert "ALWAYS the NEGATIVE `Goal`" in prompt
    assert "ALWAYS the POSITIVE `Goal`" in prompt
    assert "around that goalkeeper" in prompt
    # The team that CONCEDED a goal restarts from the center (never the scoring team).
    assert "the team that CONCEDED it restarts play from the center" in prompt
    assert (
        "If the tracked team restarts from the center, the tracked team conceded: report the "
        "NEGATIVE `Goal`." in prompt
    )
    assert (
        "If the opponent restarts from the center, the tracked team scored: report the "
        "POSITIVE `Goal`." in prompt
    )
    assert "celebrating" not in prompt


def test_default_profile_has_no_verifier_rules_and_water_polo_defines_them() -> None:
    from video_analyzer.sport import DefaultProfile

    assert DefaultProfile().verifier_rules == ()
    assert [block.title for block in WaterPoloProfile().verifier_rules] == ["Goal Polarity Check"]
