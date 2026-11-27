"""Tests for the clip-based Gemini verifier (inline 4 fps clips, batching, fail-open)."""

from __future__ import annotations

import json
import re
from collections.abc import Callable
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pytest

from tests.support import prompt_code_for
from video_analyzer.gemini.resilience import BreakerReason, GeminiCallBlockedError
from video_analyzer.media.ffmpeg_client import FFmpegCancelledError
from video_analyzer.pass1.models import (
    LabelConflictGroup,
    Pass1AnchorCandidate,
    Pass1EventCandidate,
)
from video_analyzer.schemas.draft import TimeAnchorType
from video_analyzer.sport import AnalysisContext
from video_analyzer.usage import build_gemini_usage_diagnostics
from video_analyzer.verifier import (
    UNVERIFIED_REASON,
    BaseEventVerifier,
    GeminiClipVerifier,
    VerdictKind,
    VerifierBatchResponse,
    VerifierVerdict,
)
from video_analyzer.verifier.apply import (
    apply_anchor_verdict,
    apply_event_verdict,
    mark_anchor_unverified,
    prune_orphan_stoppage_ends,
    settle_conflict_groups,
)

Verdict = dict[str, Any]
Responder = Callable[[list[int], list[Any]], list[Verdict]]


class FakeFFmpeg:
    """Records clip requests and writes a tiny payload instead of encoding video."""

    def __init__(self, failing_ids: tuple[float, ...] = ()) -> None:
        self.calls: list[dict[str, Any]] = []
        self._failing_times = failing_ids

    def cut_verification_clip(
        self, mezzanine_path, center_sec, pre_sec, post_sec, out_path, cancel_event=None
    ):
        self.calls.append(
            {
                "mezzanine": mezzanine_path,
                "center": center_sec,
                "pre": pre_sec,
                "post": post_sec,
            }
        )
        if center_sec in self._failing_times:
            raise RuntimeError("ffmpeg verification clip failed")
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_bytes(b"mp4-bytes")
        return out_path


class FakeModels:
    """Answers each batch with ``responder(item_ids, contents)`` and records the requests."""

    def __init__(self, responder: Responder, usage: bool = True) -> None:
        self._responder = responder
        self._usage = usage
        self.requests: list[list[Any]] = []

    def generate_content(self, model, contents, config):
        self.requests.append(list(contents))
        ids = [
            int(match.group(1))
            for item in contents
            if isinstance(item, str) and (match := re.match(r"Clip (\d+) ", item))
        ]
        payload = {"verdicts": self._responder(ids, contents)}
        usage = (
            SimpleNamespace(
                prompt_token_count=1000,
                candidates_token_count=100,
                thoughts_token_count=None,
                cached_content_token_count=None,
                total_token_count=1100,
            )
            if self._usage
            else None
        )
        return SimpleNamespace(text=json.dumps(payload), parsed=None, usage_metadata=usage)


def _keep_all(ids: list[int], contents: list[Any]) -> list[Verdict]:
    return [_verdict(item_id) for item_id in ids]


def _verdict(item_id: int, **overrides: Any) -> Verdict:
    verdict: Verdict = {
        "item_id": item_id,
        "verdict": "keep",
        "acting_team": "tracked",
        "is_live_play": True,
        "confidence": 0.9,
    }
    verdict.update(overrides)
    return verdict


def _event(context: AnalysisContext, name: str, time_sec: float, positive: bool = True):
    definition = next(
        d for d in context.event_catalog if d.name == name and d.is_positive == positive
    )
    return Pass1EventCandidate(
        video_time_sec=time_sec,
        definition=definition,
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=None,
        pool_zone_hint=None,
        cap_color_hint="white",
        confidence=0.7,
    )


def _anchor(time_sec: float, anchor_type: TimeAnchorType) -> Pass1AnchorCandidate:
    return Pass1AnchorCandidate(
        video_time_sec=time_sec, period_number=1, type=anchor_type, confidence=0.8
    )


def _verifier(
    responder: Responder = _keep_all,
    ffmpeg: FakeFFmpeg | None = None,
    **kwargs: Any,
) -> tuple[GeminiClipVerifier, FakeModels, FakeFFmpeg]:
    models = FakeModels(responder, usage=kwargs.pop("usage", True))
    fake_ffmpeg = ffmpeg or FakeFFmpeg()
    verifier = GeminiClipVerifier(
        api_key="test-key",
        ffmpeg_client=fake_ffmpeg,  # type: ignore[arg-type]
        client=SimpleNamespace(models=models),
        **kwargs,
    )
    return verifier, models, fake_ffmpeg


def _run(verifier: GeminiClipVerifier, context, events, anchors=(), groups=(), tmp_path=None):
    return verifier.verify(
        context,
        events,
        anchors,
        mezzanine_path=tmp_path / "mezz.mp4",
        work_dir=tmp_path,
        conflict_groups=groups,
    )


def test_verifier_implements_the_base_interface() -> None:
    assert issubclass(GeminiClipVerifier, BaseEventVerifier)


def test_clips_are_sent_inline_as_mp4_parts_with_four_fps_metadata(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models, _ = _verifier()
    events = [_event(sample_context, "Steal", 20.0), _event(sample_context, "Save", 40.0)]

    _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(models.requests) == 1
    parts = [c for c in models.requests[0] if not isinstance(c, str)]
    assert len(parts) == 2
    for part in parts:
        assert part.inline_data.mime_type == "video/mp4"
        assert part.inline_data.data == b"mp4-bytes"
        assert part.video_metadata.fps == 4
    headers = [c for c in models.requests[0] if isinstance(c, str) and c.startswith("Clip ")]
    assert len(headers) == 2
    assert "Allowed relabel targets" in headers[0]  # Steal <-> Save are confusable


def test_clip_windows_are_asymmetric_for_events_and_anchors(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, _, ffmpeg = _verifier()

    _run(
        verifier,
        sample_context,
        [_event(sample_context, "Steal", 50.0)],
        [_anchor(80.0, TimeAnchorType.PERIOD_START)],
        tmp_path=tmp_path,
    )

    by_center = {call["center"]: call for call in ffmpeg.calls}
    assert (by_center[50.0]["pre"], by_center[50.0]["post"]) == (3.0, 2.0)
    assert (by_center[80.0]["pre"], by_center[80.0]["post"]) == (2.0, 3.0)
    assert all(call["mezzanine"] == tmp_path / "mezz.mp4" for call in ffmpeg.calls)


@pytest.mark.parametrize(
    ("count", "batch_size", "expected_calls"), [(9, 4, 3), (5, 5, 1), (7, 3, 3)]
)
def test_candidates_are_batched_per_gemini_call(
    sample_context: AnalysisContext,
    tmp_path: Path,
    count: int,
    batch_size: int,
    expected_calls: int,
) -> None:
    verifier, models, _ = _verifier(batch_size=batch_size, max_candidates=100)
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(count)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(models.requests) == expected_calls
    assert len(result.events) == count
    assert all(not e.needs_review for e in result.events)


def test_keep_verdict_rewrites_acting_team_cap_color_and_confidence(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    def responder(ids, contents):
        return [_verdict(i, actor_cap_color="Red", confidence=0.97) for i in ids]

    verifier, _, _ = _verifier(responder)
    event = _event(sample_context, "Save", 40.0)

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    assert result.events == [event]
    assert event.acting_team == "tracked"
    assert event.cap_color_hint == "red"
    assert event.confidence == 0.97
    assert event.needs_review is False


def test_relabel_inside_a_confusable_set_replaces_the_definition(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    miss_code = prompt_code_for("Missed Shot", positive=False)

    def responder(ids, contents):
        return [_verdict(i, verdict="relabel", relabel_prompt_code=miss_code) for i in ids]

    verifier, _, _ = _verifier(responder)
    event = _event(sample_context, "Shot on Goal", 40.0)

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    assert result.events[0].definition.name == "Missed Shot"
    assert result.events[0].needs_review is False


def test_relabel_outside_the_confusable_sets_is_rejected_and_flagged(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    other_code = prompt_code_for("Key Pass")

    def responder(ids, contents):
        return [_verdict(i, verdict="relabel", relabel_prompt_code=other_code) for i in ids]

    verifier, _, _ = _verifier(responder)
    event = _event(sample_context, "Shot on Goal", 40.0)

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    assert result.events[0].definition.name == "Shot on Goal"
    assert result.events[0].needs_review is True
    assert "VERIFIER_RELABEL_REJECTED" in result.events[0].review_reasons


def test_drop_opponent_and_dead_play_verdicts_remove_the_event(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verdicts = {
        1: _verdict(1, verdict="drop", reason="not a steal"),
        2: _verdict(2, acting_team="opponent"),
        3: _verdict(3, is_live_play=False),
        4: _verdict(4),
    }

    verifier, _, _ = _verifier(lambda ids, contents: [verdicts[i] for i in ids])
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(4)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert result.events == [events[3]]
    joined = " ".join(result.warnings)
    assert "not a steal" in joined
    assert "opponent-team event" in joined
    assert "not live play" in joined


def test_opponent_conceded_goal_survives_the_opponent_drop_rule(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, _, _ = _verifier(
        lambda ids, contents: [_verdict(i, acting_team="opponent") for i in ids]
    )
    conceded = _event(sample_context, "Goal", 60.0, positive=False)

    result = _run(verifier, sample_context, [conceded], tmp_path=tmp_path)

    assert result.events == [conceded]
    assert conceded.acting_team == "opponent"


def test_low_verifier_confidence_flags_the_event_for_review(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, _, _ = _verifier(lambda ids, contents: [_verdict(i, confidence=0.3) for i in ids])

    result = _run(
        verifier, sample_context, [_event(sample_context, "Steal", 10.0)], tmp_path=tmp_path
    )

    assert result.events[0].needs_review is True
    assert "LOW_VERIFIER_CONFIDENCE" in result.events[0].review_reasons


def test_open_breaker_fails_open_with_unverified_candidates(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    class BlockedResilience:
        def __init__(self) -> None:
            self.calls = 0

        def call(self, fn, **kwargs):
            self.calls += 1
            raise GeminiCallBlockedError("verifier", BreakerReason.BUDGET_EXCEEDED, "limit hit")

        def backoff_delay(self, attempt, retry_after_sec=None):
            return 0.0

    resilience = BlockedResilience()
    verifier, models, ffmpeg = _verifier(resilience=resilience, batch_size=3, max_concurrency=1)  # type: ignore[arg-type]
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(6)]

    result = _run(
        verifier,
        sample_context,
        events,
        [_anchor(5.0, TimeAnchorType.PERIOD_START)],
        tmp_path=tmp_path,
    )

    assert models.requests == []
    assert result.events == events
    assert all(e.needs_review and UNVERIFIED_REASON in e.review_reasons for e in result.events)
    assert result.anchors[0].needs_review is True
    # The first batch trips the breaker; the second batch is skipped without cutting clips.
    assert resilience.calls == 1
    assert len(ffmpeg.calls) == 3
    assert any("budget_exceeded" in w for w in result.warnings)


def test_candidates_above_the_limit_stay_unverified(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models, _ = _verifier(max_candidates=3, batch_size=3)
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(5)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(models.requests) == 1
    assert [bool(e.needs_review) for e in result.events] == [False, False, False, True, True]
    assert all(UNVERIFIED_REASON in e.review_reasons for e in result.events[3:])
    assert any("VERIFIER_MAX_CANDIDATES" in w for w in result.warnings)


def test_missing_verdict_and_failed_clip_leave_only_those_candidates_unverified(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    def responder(ids, contents):
        return [_verdict(i) for i in ids if i != 2]  # the model skips clip 2

    verifier, _, _ = _verifier(responder, ffmpeg=FakeFFmpeg(failing_ids=(30.0,)))
    events = [_event(sample_context, "Steal", t) for t in (10.0, 20.0, 30.0, 40.0)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    flags = [UNVERIFIED_REASON in e.review_reasons for e in result.events]
    assert flags == [False, True, True, False]
    assert any("could not cut clip 3" in w for w in result.warnings)


def test_api_errors_fail_open(sample_context: AnalysisContext, tmp_path: Path) -> None:
    class ExplodingModels:
        def generate_content(self, **kwargs):
            raise ValueError("server exploded")

    verifier = GeminiClipVerifier(
        api_key="k",
        ffmpeg_client=FakeFFmpeg(),  # type: ignore[arg-type]
        client=SimpleNamespace(models=ExplodingModels()),
    )
    event = _event(sample_context, "Steal", 10.0)

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    assert result.events == [event]
    assert UNVERIFIED_REASON in event.review_reasons


def test_malformed_output_is_retried_then_fails_open(
    sample_context: AnalysisContext, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr("video_analyzer.verifier.gemini_verifier.time.sleep", lambda _: None)
    calls: list[int] = []

    class GarbageModels:
        def generate_content(self, **kwargs):
            calls.append(1)
            return SimpleNamespace(text="not json", parsed=None, usage_metadata=None)

    verifier = GeminiClipVerifier(
        api_key="k",
        ffmpeg_client=FakeFFmpeg(),  # type: ignore[arg-type]
        client=SimpleNamespace(models=GarbageModels()),
        max_attempts=2,
    )
    event = _event(sample_context, "Steal", 10.0)

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    assert len(calls) == 2
    assert UNVERIFIED_REASON in result.events[0].review_reasons


def test_token_usage_is_collected_per_call_and_priced_in_the_verifier_slot(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models, _ = _verifier(batch_size=3, max_concurrency=1)
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(6)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(models.requests) == 2
    assert len(result.usages) == 2
    diagnostics = build_gemini_usage_diagnostics(
        pass1_model="gemini-4-hydrogen",
        pass1_usages=[],
        pass2_model="gemini-4-hydrogen",
        pass2_usages=[],
        pricing_table={"gemini-4-hydrogen": {"input": 1.0, "output": 1.0}},
        verifier_model="gemini-4-hydrogen",
        verifier_usages=result.usages,
    )
    assert diagnostics.verifier.usage.call_count == 2
    assert diagnostics.verifier.usage.prompt_tokens == 2000
    assert diagnostics.total.call_count == 2


def test_conflict_group_headers_name_the_competing_label_and_duplicates_are_settled(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    sog_code = prompt_code_for("Shot on Goal")
    miss_code = prompt_code_for("Missed Shot", positive=False)
    sog = _event(sample_context, "Shot on Goal", 112.0)
    miss = _event(sample_context, "Missed Shot", 112.5, positive=False)
    sog.confidence, miss.confidence = 0.8, 0.6
    group = LabelConflictGroup(candidates=[sog, miss])

    def responder(ids, contents):
        # Both clips are judged to show the same shot on goal: clip 2 is relabeled to it.
        return [
            _verdict(1, confidence=0.9),
            _verdict(2, verdict="relabel", relabel_prompt_code=sog_code, confidence=0.7),
        ]

    verifier, models, _ = _verifier(responder)

    result = _run(verifier, sample_context, [sog, miss], groups=[group], tmp_path=tmp_path)

    headers = [c for c in models.requests[0] if isinstance(c, str) and c.startswith("Clip ")]
    assert miss_code in headers[0]
    assert sog_code in headers[1]
    assert "competing label" in headers[0]
    assert result.events == [sog]
    assert any("cross-chunk label conflict" in w for w in result.warnings)


def test_settle_conflict_groups_keeps_distinct_labels_untouched(
    sample_context: AnalysisContext,
) -> None:
    sog = _event(sample_context, "Shot on Goal", 112.0)
    miss = _event(sample_context, "Missed Shot", 112.5, positive=False)

    kept, warnings = settle_conflict_groups(
        [sog, miss], [LabelConflictGroup(candidates=[sog, miss])]
    )

    assert kept == [sog, miss]
    assert warnings == []


def test_dropping_a_stoppage_start_prunes_its_orphaned_end(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    def responder(ids, contents):
        # Item 1 is the StoppageStart (anchors are verified first).
        return [_verdict(i, verdict="drop" if i == 1 else "keep") for i in ids]

    verifier, _, _ = _verifier(responder)
    anchors = [
        _anchor(60.0, TimeAnchorType.STOPPAGE_START),
        _anchor(75.0, TimeAnchorType.STOPPAGE_END),
    ]

    result = _run(verifier, sample_context, [], anchors, tmp_path=tmp_path)

    assert result.anchors == []
    assert any("orphan StoppageEnd" in w for w in result.warnings)


def test_prune_orphan_stoppage_ends_keeps_well_formed_pairs() -> None:
    anchors = [
        _anchor(10.0, TimeAnchorType.STOPPAGE_START),
        _anchor(20.0, TimeAnchorType.STOPPAGE_END),
        _anchor(30.0, TimeAnchorType.STOPPAGE_START),
    ]

    kept, warnings = prune_orphan_stoppage_ends(anchors)

    assert kept == anchors
    assert warnings == []


def test_cancellation_while_cutting_clips_propagates(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    class CancellingFFmpeg(FakeFFmpeg):
        def cut_verification_clip(self, *args, **kwargs):
            raise FFmpegCancelledError("cancelled")

    verifier, _, _ = _verifier(ffmpeg=CancellingFFmpeg())
    events = [_event(sample_context, "Steal", 10.0)]

    with pytest.raises(FFmpegCancelledError):
        _run(verifier, sample_context, events, tmp_path=tmp_path)


def test_verdict_models_accept_the_documented_response_shape() -> None:
    payload = VerifierBatchResponse.model_validate(
        {"verdicts": [_verdict(1, verdict="relabel", relabel_prompt_code="EVT_X")]}
    )

    assert payload.verdicts[0].verdict == VerdictKind.RELABEL
    assert payload.verdicts[0].relabel_prompt_code == "EVT_X"


def test_verdicts_for_ids_outside_the_submitted_batch_are_rejected(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    def responder(ids, contents):
        # Besides its own clips the model also answers for item 99 (unknown) and item 5, which
        # belongs to a different (here: skipped) candidate and asks to drop it.
        return [
            *[_verdict(i) for i in ids if i != 2],
            _verdict(5, verdict="drop"),
            _verdict(99, verdict="drop"),
        ]

    verifier, _, _ = _verifier(responder, batch_size=2, max_candidates=4)
    events = [_event(sample_context, "Steal", 10.0 * (i + 1)) for i in range(5)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(result.events) == 5  # the foreign drop verdicts never reach other candidates
    unverified = [UNVERIFIED_REASON in e.review_reasons for e in result.events]
    assert unverified == [False, True, False, False, True]  # missing verdict + skipped stay open
    assert any("outside the submitted batch" in w and "99" in w for w in result.warnings)


def test_completed_calls_without_usage_metadata_are_counted_as_unknown_usage(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, models, _ = _verifier(usage=False, batch_size=3, max_concurrency=1)
    events = [_event(sample_context, "Steal", 10.0 + 10 * i) for i in range(6)]

    result = _run(verifier, sample_context, events, tmp_path=tmp_path)

    assert len(models.requests) == 2
    assert len(result.usages) == 2
    diagnostics = build_gemini_usage_diagnostics(
        pass1_model="gemini-4-hydrogen",
        pass1_usages=[],
        pass2_model="gemini-4-hydrogen",
        pass2_usages=[],
        pricing_table={"gemini-4-hydrogen": {"input": 1.0, "output": 1.0}},
        verifier_model="gemini-4-hydrogen",
        verifier_usages=result.usages,
    )
    assert diagnostics.verifier.usage.call_count == 2
    assert diagnostics.verifier.usage.prompt_tokens is None
    assert diagnostics.verifier.usage.total_tokens is None
    assert diagnostics.total.call_count == 2
    assert diagnostics.estimated_cost is not None
    assert diagnostics.estimated_cost.total_cost_usd is None


def test_malformed_attempts_without_metadata_still_count_every_completed_call(
    sample_context: AnalysisContext, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr("video_analyzer.verifier.gemini_verifier.time.sleep", lambda _: None)

    class GarbageModels:
        def generate_content(self, **kwargs):
            return SimpleNamespace(text="not json", parsed=None, usage_metadata=None)

    verifier = GeminiClipVerifier(
        api_key="k",
        ffmpeg_client=FakeFFmpeg(),  # type: ignore[arg-type]
        client=SimpleNamespace(models=GarbageModels()),
        max_attempts=2,
    )
    event = _event(sample_context, "Steal", 10.0)

    result = _run(verifier, sample_context, [event], tmp_path=tmp_path)

    # Both attempts completed a Gemini call, so both are counted despite the missing metadata.
    assert [u.call_count for u in result.usages if u is not None] == [1, 1]


def _bboxed_event(context: AnalysisContext, name: str, positive: bool = True):
    event = _event(context, name, 40.0, positive)
    event.actor_bbox_normalized = [0.1, 0.2, 0.3, 0.4]
    return event


def _relabel_verdict(prompt_code: str, **overrides: Any) -> VerifierVerdict:
    return VerifierVerdict.model_validate(
        _verdict(1, verdict="relabel", relabel_prompt_code=prompt_code, **overrides)
    )


def _catalog(context: AnalysisContext) -> dict[str, Any]:
    return {d.prompt_code: d for d in context.event_catalog}


def test_same_actor_relabel_keeps_the_actor_bbox_and_the_observed_cap_color(
    sample_context: AnalysisContext,
) -> None:
    event = _bboxed_event(sample_context, "Shot on Goal")
    verdict = _relabel_verdict(prompt_code_for("Missed Shot", positive=False))

    kept, _ = apply_event_verdict(
        event, verdict, _catalog(sample_context), sample_context.profile, "white"
    )

    assert kept is event
    assert event.definition.name == "Missed Shot"
    assert event.actor_bbox_normalized == [0.1, 0.2, 0.3, 0.4]
    assert event.cap_color_hint == "white"


def test_goalkeeper_status_relabel_clears_the_bbox_and_rederives_the_cap_color(
    sample_context: AnalysisContext,
) -> None:
    event = _bboxed_event(sample_context, "Steal")
    verdict = _relabel_verdict(prompt_code_for("Save"))

    apply_event_verdict(event, verdict, _catalog(sample_context), sample_context.profile, "white")

    assert event.definition.name == "Save"
    assert event.actor_bbox_normalized is None
    assert event.cap_color_hint == "red"  # goalkeeper cap color replaces the field player's hint


def test_save_relabeled_to_steal_restores_the_tracked_team_cap_color(
    sample_context: AnalysisContext,
) -> None:
    event = _bboxed_event(sample_context, "Save")
    event.cap_color_hint = "red"
    verdict = _relabel_verdict(prompt_code_for("Steal"))

    apply_event_verdict(event, verdict, _catalog(sample_context), sample_context.profile, "white")

    assert event.actor_bbox_normalized is None
    assert event.cap_color_hint == "white"


def test_goal_polarity_relabel_clears_the_bbox_in_both_directions(
    sample_context: AnalysisContext,
) -> None:
    catalog = _catalog(sample_context)
    profile = sample_context.profile
    positive = _bboxed_event(sample_context, "Goal")
    negative = _bboxed_event(sample_context, "Goal", positive=False)

    apply_event_verdict(
        positive, _relabel_verdict(prompt_code_for("Goal", positive=False)), catalog, profile
    )
    apply_event_verdict(negative, _relabel_verdict(prompt_code_for("Goal")), catalog, profile)

    assert positive.definition.is_positive is False
    assert positive.actor_bbox_normalized is None
    assert positive.cap_color_hint == "red"
    assert negative.definition.is_positive is True
    assert negative.actor_bbox_normalized is None


def test_verifier_observed_cap_color_wins_over_the_derived_expectation(
    sample_context: AnalysisContext,
) -> None:
    event = _bboxed_event(sample_context, "Steal")
    verdict = _relabel_verdict(prompt_code_for("Save"), actor_cap_color=" Red ")

    apply_event_verdict(event, verdict, _catalog(sample_context), sample_context.profile, "white")

    assert event.cap_color_hint == "red"


def test_role_change_without_any_expectation_drops_the_stale_cap_color(
    sample_context: AnalysisContext,
) -> None:
    event = _bboxed_event(sample_context, "Save")
    event.cap_color_hint = "red"
    verdict = _relabel_verdict(prompt_code_for("Steal"))

    apply_event_verdict(event, verdict, _catalog(sample_context), sample_context.profile, None)

    assert event.cap_color_hint is None


def test_keep_verdict_leaves_bbox_and_cap_color_untouched_without_new_information(
    sample_context: AnalysisContext,
) -> None:
    event = _bboxed_event(sample_context, "Steal")
    verdict = VerifierVerdict.model_validate(_verdict(1))

    apply_event_verdict(event, verdict, _catalog(sample_context), sample_context.profile, None)

    assert event.actor_bbox_normalized == [0.1, 0.2, 0.3, 0.4]
    assert event.cap_color_hint == "white"


def test_only_a_keep_verdict_marks_an_anchor_as_verified() -> None:
    kept = _anchor(10.0, TimeAnchorType.STOPPAGE_START)
    dropped = _anchor(20.0, TimeAnchorType.STOPPAGE_START)
    unverified = _anchor(30.0, TimeAnchorType.STOPPAGE_START)
    unverified.verified = True

    apply_anchor_verdict(kept, VerifierVerdict.model_validate(_verdict(1)))
    apply_anchor_verdict(dropped, VerifierVerdict.model_validate(_verdict(2, verdict="drop")))
    mark_anchor_unverified(unverified)

    assert kept.verified is True
    assert dropped.verified is False
    assert unverified.verified is False
    assert unverified.needs_review is True


def test_verifier_confirms_a_trailing_stoppage_only_through_a_keep_verdict(
    sample_context: AnalysisContext, tmp_path: Path
) -> None:
    verifier, _, _ = _verifier()
    anchors = [_anchor(60.0, TimeAnchorType.STOPPAGE_START)]

    result = _run(verifier, sample_context, [], anchors, tmp_path=tmp_path)

    assert result.anchors[0].verified is True
