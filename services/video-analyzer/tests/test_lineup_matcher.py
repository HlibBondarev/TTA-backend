"""Tests for roster-constrained lineup resolution and non-guessing confidence gating."""

from __future__ import annotations

from uuid import uuid4

from video_analyzer.pass1.models import (
    Pass1ChunkResult,
    Pass1EventCandidate,
    Pass1RawAnchor,
    Pass1RawEvent,
)
from video_analyzer.pass2.cap_resolver import CapResolutionResult
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.schemas.draft import TimeAnchorType
from video_analyzer.schemas.request import AnalyzeJobRequest, LineupEntry, MatchSnapshot


def _event_candidate(snapshot: MatchSnapshot, short_name: str = "G") -> Pass1EventCandidate:
    defn = next(d for d in snapshot.event_definitions if d.short_name == short_name)
    return Pass1EventCandidate(
        video_time_sec=55.0,
        definition=defn,
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=[0.4, 0.4, 0.5, 0.6],
        pool_zone_hint="center",
        cap_color_hint="white",
        confidence=0.92,
    )


def test_lineup_matcher_resolves_high_confidence_cap(sample_snapshot: MatchSnapshot) -> None:
    matcher = LineupMatcher()
    cand = _event_candidate(sample_snapshot)
    cap_res = CapResolutionResult(
        resolved_cap_number=7,
        confidence=0.91,
        candidate_scores={7: 0.91},
        reasoning="5/6 sharp crops matched cap #7",
    )

    outcome = matcher.resolve(cand, cap_res, sample_snapshot, min_cap_confidence=0.80)
    expected_entry = next(
        e
        for e in sample_snapshot.lineup
        if e.team_id == sample_snapshot.tracked_team.id and e.number == 7
    )

    assert outcome.match_lineup_id == expected_entry.match_lineup_id
    assert outcome.resolved_cap_number == 7
    assert outcome.needs_review is False
    assert outcome.review_reasons == []


def test_lineup_matcher_never_guesses_on_low_confidence(sample_snapshot: MatchSnapshot) -> None:
    matcher = LineupMatcher()
    cand = _event_candidate(sample_snapshot)
    cap_res = CapResolutionResult(
        resolved_cap_number=4,
        confidence=0.52,
        candidate_scores={4: 0.52, 11: 0.34},
        reasoning="Partial digit visibility due to splash",
    )

    outcome = matcher.resolve(cand, cap_res, sample_snapshot, min_cap_confidence=0.80)

    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert "LOW_CAP_CONFIDENCE" in outcome.review_reasons
    assert [c.cap_number for c in outcome.candidate_lineup_ids] == [4, 11]


def test_lineup_matcher_flags_duplicate_cap_number_collision(
    sample_snapshot: MatchSnapshot,
) -> None:
    duplicate_entry = LineupEntry(
        match_lineup_id=uuid4(),
        team_id=sample_snapshot.tracked_team.id,
        number=7,
        first_name="Duplicate",
        last_name="Seven",
        position_name="Driver",
        is_goalkeeper=False,
    )
    modified = sample_snapshot.model_copy(
        update={"lineup": [*sample_snapshot.lineup, duplicate_entry]}
    )
    cand = _event_candidate(modified)
    cap_res = CapResolutionResult(
        resolved_cap_number=7,
        confidence=0.96,
        candidate_scores={7: 0.96},
        reasoning="Clear cap #7",
    )

    outcome = LineupMatcher().resolve(cand, cap_res, modified, min_cap_confidence=0.80)

    assert outcome.match_lineup_id is None
    assert outcome.needs_review is True
    assert "DUPLICATE_CAP_NUMBER_IN_LINEUP" in outcome.review_reasons
    assert len([c for c in outcome.candidate_lineup_ids if c.cap_number == 7]) == 2


def test_lineup_matcher_prioritizes_goalkeeper_on_save_without_tracklet(
    sample_snapshot: MatchSnapshot,
) -> None:
    cand = _event_candidate(sample_snapshot, short_name="ASV")  # Save
    outcome = LineupMatcher().resolve(cand, None, sample_snapshot, min_cap_confidence=0.80)

    assert outcome.match_lineup_id is None
    assert outcome.needs_review is True
    assert "NO_PLAYER_TRACKLET_MATCHED" in outcome.review_reasons
    assert outcome.candidate_lineup_ids[0].cap_number in (1, 13)


def test_lineup_matcher_never_auto_confirms_unhinted_match(
    sample_snapshot: MatchSnapshot,
) -> None:
    cand = _event_candidate(sample_snapshot)
    cand.actor_bbox_normalized = None
    cap_res = CapResolutionResult(
        resolved_cap_number=7,
        confidence=0.95,
        candidate_scores={7: 0.95, 4: 0.40},
        reasoning="Sharp cap #7 on fallback tracklet",
    )

    outcome = LineupMatcher().resolve(
        cand,
        cap_res,
        sample_snapshot,
        min_cap_confidence=0.80,
        has_spatial_hint=False,
    )

    assert outcome.match_lineup_id is None
    assert outcome.resolved_cap_number is None
    assert outcome.needs_review is True
    assert "MISSING_ACTOR_SPATIAL_HINT" in outcome.review_reasons
    assert [c.cap_number for c in outcome.candidate_lineup_ids] == [7, 4]


def test_orchestrator_leaves_unhinted_event_unresolved_with_candidates(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    from tests.test_orchestrator_and_api import (
        MockCapResolver,
        MockDetector,
        MockFFmpegClient,
    )
    from video_analyzer.config import Settings
    from video_analyzer.media.ffmpeg_client import VideoChunk
    from video_analyzer.pass1.gemini_client import BasePass1Analyzer
    from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator

    class UnhintedPass1Analyzer(BasePass1Analyzer):
        def analyze_chunk(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> Pass1ChunkResult:
            return Pass1ChunkResult(
                time_anchors=[
                    Pass1RawAnchor(
                        time_sec=5.0,
                        period_number=1,
                        type=TimeAnchorType.PERIOD_START,
                        confidence=0.95,
                    ),
                    Pass1RawAnchor(
                        time_sec=220.0,
                        period_number=1,
                        type=TimeAnchorType.PERIOD_END,
                        confidence=0.95,
                    ),
                ],
                period_events=[
                    Pass1RawEvent(
                        time_sec=45.0,
                        prompt_code="EVT_01_G_POS",
                        period_number=1,
                        actor_bbox_normalized=None,
                        confidence=0.94,
                    )
                ],
            )

    orchestrator = VideoAnalysisOrchestrator(
        settings=Settings(_env_file=None, internal_api_key="test-key"),
        ffmpeg_client=MockFFmpegClient(),  # type: ignore[arg-type]
        pass1_analyzer=UnhintedPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=MockCapResolver(),
    )

    draft = orchestrator.run(sample_job_request)
    assert len(draft.events) == 1
    evt = draft.events[0]
    assert evt.match_lineup_id is None
    assert evt.resolved_cap_number is None
    assert evt.needs_review is True
    assert "MISSING_ACTOR_SPATIAL_HINT" in evt.review_reasons
    assert [c.cap_number for c in evt.candidate_lineup_ids] == [7]
    assert draft.diagnostics.resolved_actor_count == 0
    assert draft.diagnostics.unresolved_actor_count == 1
