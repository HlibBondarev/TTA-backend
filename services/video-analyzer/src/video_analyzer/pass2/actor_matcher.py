"""Match a Pass 1 actor hint to a Pass 2 tracklet using spatial, team and ball evidence."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass, field
from math import hypot

from video_analyzer.media.ffmpeg_client import ExtractedFrame
from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.failure_reasons import Pass2FailureReason
from video_analyzer.pass2.team_classifier import (
    TeamClass,
    TeamClassificationSpec,
    TeamClassifier,
    TeamEvidence,
    observation_head_zone,
)
from video_analyzer.pass2.tracker import (
    BBox,
    FrameDetection,
    Tracklet,
    TrackletObservation,
    _bbox_center,
    _bbox_iou,
)
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.sport.profile import CapZoneGeometry

MIN_SPATIAL_SCORE: float = 0.15

TRACKED_TEAM_BONUS: float = 0.15
"""Team-evidence bonus for a confidently tracked-team tracklet (scaled by vote share)."""
GOALKEEPER_EVENT_BONUS: float = 0.20
"""Bonus for goalkeeper evidence on a goalkeeper event."""
GOALKEEPER_FIELD_PENALTY: float = 0.10
"""Soft penalty for a pure goalkeeper tracklet on a field-player event."""

BALL_WINDOW_BEFORE_SEC: float = 1.0
BALL_WINDOW_AFTER_SEC: float = 0.5
MIN_BALL_DETECTIONS: int = 3
"""Ball evidence is ignored unless at least this many detections fall in the window."""
MAX_BALL_BONUS: float = 0.10
BALL_PROXIMITY_RADIUS: float = 1.0
"""Ball-to-head distance, in box heights, at which the ball bonus decays to zero."""

TIME_PENALTY_PER_SEC: float = 0.05
MAX_TIME_PENALTY: float = 0.25
PROXIMITY_DISTANCE_GAIN: float = 4.0

EXCLUDED_OPPONENT = "OPPONENT_TEAM"
EXCLUDED_NOT_GOALKEEPER = "NOT_GOALKEEPER"
EXCLUDED_LOW_SPATIAL = "LOW_SPATIAL_SCORE"


@dataclass(frozen=True, slots=True)
class MatchedTracklet:
    """Best tracklet for an event plus the evidence that selected it."""

    tracklet: Tracklet
    match_score: float
    nearest_observation: TrackletObservation
    has_spatial_hint: bool = True
    is_tied: bool = False
    team_class: TeamClass = TeamClass.UNKNOWN
    team_evidence: TeamEvidence | None = None


@dataclass(frozen=True, slots=True)
class CandidateBreakdown:
    """Per-tracklet score components, kept for debug artifacts."""

    track_id: int
    team_evidence: TeamEvidence = field(default_factory=TeamEvidence)
    iou: float = 0.0
    proximity: float = 0.0
    spatial_score: float = 0.0
    team_bonus: float = 0.0
    ball_bonus: float = 0.0
    time_penalty: float = 0.0
    exclusion_reason: str | None = None
    final_score: float | None = None

    def with_exclusion(self, reason: str) -> CandidateBreakdown:
        """Return a copy of this breakdown excluded for ``reason``."""
        return CandidateBreakdown(
            track_id=self.track_id,
            team_evidence=self.team_evidence,
            iou=self.iou,
            proximity=self.proximity,
            spatial_score=self.spatial_score,
            team_bonus=self.team_bonus,
            ball_bonus=self.ball_bonus,
            time_penalty=self.time_penalty,
            exclusion_reason=reason,
            final_score=self.final_score,
        )

    def with_scores(
        self,
        team_bonus: float,
        ball_bonus: float,
        time_penalty: float,
        final_score: float | None = None,
        exclusion_reason: str | None = None,
    ) -> CandidateBreakdown:
        """Return a copy carrying the score components and the final score or exclusion."""
        return CandidateBreakdown(
            track_id=self.track_id,
            team_evidence=self.team_evidence,
            iou=self.iou,
            proximity=self.proximity,
            spatial_score=self.spatial_score,
            team_bonus=team_bonus,
            ball_bonus=ball_bonus,
            time_penalty=time_penalty,
            exclusion_reason=exclusion_reason,
            final_score=final_score,
        )

    def to_debug_dict(self) -> dict[str, object]:
        """Return a JSON-serializable score breakdown."""
        return {
            "trackId": self.track_id,
            "team": self.team_evidence.to_debug_dict(),
            "iou": round(self.iou, 4),
            "proximity": round(self.proximity, 4),
            "spatialScore": round(self.spatial_score, 4),
            "teamBonus": round(self.team_bonus, 4),
            "ballBonus": round(self.ball_bonus, 4),
            "timePenalty": round(self.time_penalty, 4),
            "exclusionReason": self.exclusion_reason,
            "finalScore": self.final_score,
        }


@dataclass(frozen=True, slots=True)
class ActorMatchResult:
    """Outcome of :meth:`ActorMatcher.match_detailed`."""

    matched: MatchedTracklet | None
    failure_reason: Pass2FailureReason | None = None
    breakdowns: list[CandidateBreakdown] = field(default_factory=list)
    opponent_exclusions: int = 0
    ball_detections_in_window: int = 0


@dataclass(frozen=True, slots=True)
class _MatchInputs:
    """Per-event values shared by every tracklet evaluation."""

    target_bbox_px: BBox | None
    diag: float
    is_goalkeeper_event: bool
    event_time_sec: float
    spec: TeamClassificationSpec
    frames_by_index: dict[int, ExtractedFrame] | None
    ball_window: list[FrameDetection]


class ActorMatcher:
    """Scores tracklets against a Pass 1 actor hint and the tracked team's identity."""

    def __init__(
        self,
        min_spatial_score: float = MIN_SPATIAL_SCORE,
        classifier: TeamClassifier | None = None,
    ) -> None:
        self._min_spatial_score = min_spatial_score
        self._classifier = classifier or TeamClassifier()

    def match(
        self,
        candidate: Pass1EventCandidate,
        tracklets: list[Tracklet],
        context: AnalysisContext,
        frame_width: int,
        frame_height: int,
    ) -> MatchedTracklet | None:
        """Return the best tracklet for ``candidate`` or ``None`` (label evidence only)."""
        return self.match_detailed(candidate, tracklets, context, frame_width, frame_height).matched

    def match_detailed(
        self,
        candidate: Pass1EventCandidate,
        tracklets: list[Tracklet],
        context: AnalysisContext,
        frame_width: int,
        frame_height: int,
        *,
        frames: Sequence[ExtractedFrame] | None = None,
        ball_detections: Sequence[FrameDetection] | None = None,
    ) -> ActorMatchResult:
        """Match the actor and report why a tracklet was chosen, excluded or absent.

        Args:
            candidate: Pass 1 event candidate with an optional normalized actor box.
            tracklets: Tracklets from the Pass 2 window.
            context: Analysis context providing hints and the sport profile.
            frame_width: Native frame width in pixels.
            frame_height: Native frame height in pixels.
            frames: Window frames; enables HSV cap-color voting when provided.
            ball_detections: Ball detections of the window for the soft proximity bonus.

        Returns:
            :class:`ActorMatchResult`. Opponent tracklets are never selected.
        """
        if not tracklets or frame_width <= 0 or frame_height <= 0:
            return ActorMatchResult(
                None,
                Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED,
                ball_detections_in_window=len(
                    _ball_window(ball_detections, candidate.video_time_sec)
                ),
            )
        inputs = self._build_inputs(
            candidate, context, frame_width, frame_height, frames, ball_detections
        )
        geometry = inputs.spec.geometry
        breakdowns = [
            self._evaluate(tracklet, inputs, geometry)
            for tracklet in tracklets
            if tracklet.observations
        ]
        return self._finalize(
            candidate,
            tracklets,
            breakdowns,
            inputs,
            has_spatial_hint=inputs.target_bbox_px is not None,
        )

    def _build_inputs(
        self,
        candidate: Pass1EventCandidate,
        context: AnalysisContext,
        frame_width: int,
        frame_height: int,
        frames: Sequence[ExtractedFrame] | None,
        ball_detections: Sequence[FrameDetection] | None,
    ) -> _MatchInputs:
        target_bbox_px: BBox | None = None
        if candidate.actor_bbox_normalized is not None:
            nx1, ny1, nx2, ny2 = candidate.actor_bbox_normalized
            target_bbox_px = (
                nx1 * frame_width,
                ny1 * frame_height,
                nx2 * frame_width,
                ny2 * frame_height,
            )
        event_time = candidate.video_time_sec
        return _MatchInputs(
            target_bbox_px=target_bbox_px,
            diag=hypot(frame_width, frame_height),
            is_goalkeeper_event=context.profile.is_goalkeeper_event(candidate.definition),
            event_time_sec=event_time,
            spec=TeamClassificationSpec.from_context(context),
            frames_by_index={f.index: f for f in frames} if frames is not None else None,
            ball_window=_ball_window(ball_detections, event_time),
        )

    def _evaluate(
        self, tracklet: Tracklet, inputs: _MatchInputs, geometry: CapZoneGeometry
    ) -> CandidateBreakdown:
        obs = tracklet.observation_nearest_to(inputs.event_time_sec)
        if obs is None:
            return CandidateBreakdown(
                track_id=tracklet.track_id, exclusion_reason=EXCLUDED_LOW_SPATIAL
            )
        iou, proximity, spatial = self._spatial_components(obs, inputs)
        has_overlap = iou > 0.0
        if (
            inputs.target_bbox_px is not None
            and not has_overlap
            and spatial < self._min_spatial_score
        ):
            return CandidateBreakdown(
                track_id=tracklet.track_id,
                iou=iou,
                proximity=proximity,
                spatial_score=spatial,
                exclusion_reason=EXCLUDED_LOW_SPATIAL,
            )
        evidence = self._classifier.classify(
            tracklet, inputs.event_time_sec, inputs.frames_by_index, inputs.spec
        )
        base = CandidateBreakdown(
            track_id=tracklet.track_id,
            team_evidence=evidence,
            iou=iou,
            proximity=proximity,
            spatial_score=spatial,
        )
        exclusion = _team_exclusion(evidence, inputs.is_goalkeeper_event)
        if exclusion is not None:
            return base.with_exclusion(exclusion)
        return self._score(tracklet, obs, base, inputs, geometry, has_overlap)

    def _score(
        self,
        tracklet: Tracklet,
        obs: TrackletObservation,
        base: CandidateBreakdown,
        inputs: _MatchInputs,
        geometry: CapZoneGeometry,
        has_overlap: bool,
    ) -> CandidateBreakdown:
        team_bonus = _team_bonus(base.team_evidence, inputs.is_goalkeeper_event)
        ball_bonus = _ball_bonus(tracklet, inputs.ball_window, geometry)
        time_penalty = min(
            MAX_TIME_PENALTY,
            abs(obs.video_time_sec - inputs.event_time_sec) * TIME_PENALTY_PER_SEC,
        )
        if inputs.target_bbox_px is None:
            stability = min(0.4, len(tracklet.observations) * 0.03)
            raw = (
                tracklet.mean_confidence * 0.4 + stability + team_bonus + ball_bonus - time_penalty
            )
            total = round(max(0.0, raw), 4)
        else:
            raw = base.spatial_score + team_bonus + ball_bonus - time_penalty
            total = round(max(0.0, min(1.0, raw)), 4)
            if total <= 0.0 or (not has_overlap and total < self._min_spatial_score):
                return base.with_scores(
                    team_bonus,
                    ball_bonus,
                    time_penalty,
                    exclusion_reason=EXCLUDED_LOW_SPATIAL,
                )
        return base.with_scores(team_bonus, ball_bonus, time_penalty, final_score=total)

    @staticmethod
    def _spatial_components(
        obs: TrackletObservation, inputs: _MatchInputs
    ) -> tuple[float, float, float]:
        """Return ``(iou, proximity, spatial_score)``; all zero without a spatial hint."""
        target = inputs.target_bbox_px
        if target is None:
            return 0.0, 0.0, 0.0
        iou = _bbox_iou(target, obs.bbox_xyxy)
        tcx, tcy = _bbox_center(target)
        ocx, ocy = _bbox_center(obs.bbox_xyxy)
        norm_dist = hypot(tcx - ocx, tcy - ocy) / max(1.0, inputs.diag)
        proximity = max(0.0, 1.0 - norm_dist * PROXIMITY_DISTANCE_GAIN)
        return iou, proximity, iou * 0.55 + proximity * 0.35

    def _finalize(
        self,
        candidate: Pass1EventCandidate,
        tracklets: list[Tracklet],
        breakdowns: list[CandidateBreakdown],
        inputs: _MatchInputs,
        *,
        has_spatial_hint: bool,
    ) -> ActorMatchResult:
        by_id = {t.track_id: t for t in tracklets}
        opponent_exclusions = sum(b.exclusion_reason == EXCLUDED_OPPONENT for b in breakdowns)
        scored = [b for b in breakdowns if b.final_score is not None]
        ball_in_window = len(inputs.ball_window)
        if not scored:
            return ActorMatchResult(
                matched=None,
                failure_reason=_failure_reason(breakdowns),
                breakdowns=breakdowns,
                opponent_exclusions=opponent_exclusions,
                ball_detections_in_window=ball_in_window,
            )
        best = max(scored, key=lambda b: b.final_score or 0.0)
        tracklet = by_id[best.track_id]
        obs = tracklet.observation_nearest_to(candidate.video_time_sec) or tracklet.observations[0]
        is_tied = sum(b.final_score == best.final_score for b in scored) > 1
        matched = MatchedTracklet(
            tracklet=tracklet,
            match_score=best.final_score or 0.0,
            nearest_observation=obs,
            has_spatial_hint=has_spatial_hint,
            is_tied=is_tied,
            team_class=best.team_evidence.team_class,
            team_evidence=best.team_evidence,
        )
        return ActorMatchResult(
            matched=matched,
            breakdowns=breakdowns,
            opponent_exclusions=opponent_exclusions,
            ball_detections_in_window=ball_in_window,
        )


def _ball_window(
    ball_detections: Sequence[FrameDetection] | None, event_time_sec: float
) -> list[FrameDetection]:
    """Return the ball detections inside ``[t - 1.0 s, t + 0.5 s]`` around the event."""
    return [
        det
        for det in (ball_detections or ())
        if event_time_sec - BALL_WINDOW_BEFORE_SEC
        <= det.video_time_sec
        <= event_time_sec + BALL_WINDOW_AFTER_SEC
    ]


def _team_exclusion(evidence: TeamEvidence, is_goalkeeper_event: bool) -> str | None:
    """Return the hard-exclusion reason for a tracklet, or ``None`` when it stays eligible."""
    if evidence.team_class == TeamClass.OPPONENT:
        return EXCLUDED_OPPONENT
    if is_goalkeeper_event and not evidence.goalkeeper_evidence:
        return EXCLUDED_NOT_GOALKEEPER
    return None


def _team_bonus(evidence: TeamEvidence, is_goalkeeper_event: bool) -> float:
    bonus = 0.0
    if evidence.team_class in (TeamClass.TRACKED, TeamClass.GOALKEEPER):
        bonus += TRACKED_TEAM_BONUS * evidence.tracked_share
    if is_goalkeeper_event and evidence.goalkeeper_evidence:
        bonus += GOALKEEPER_EVENT_BONUS
    if not is_goalkeeper_event and evidence.goalkeeper_evidence:
        bonus -= GOALKEEPER_FIELD_PENALTY
    return bonus


def _failure_reason(breakdowns: list[CandidateBreakdown]) -> Pass2FailureReason:
    """Map an empty selection to a failure reason.

    ``NO_TRACKED_TEAM_ACTOR`` is reported only when spatially valid tracklets existed and
    every one of them was excluded as an opponent.
    """
    spatially_valid = [b for b in breakdowns if b.exclusion_reason != EXCLUDED_LOW_SPATIAL]
    if spatially_valid and all(b.exclusion_reason == EXCLUDED_OPPONENT for b in spatially_valid):
        return Pass2FailureReason.NO_TRACKED_TEAM_ACTOR
    return Pass2FailureReason.NO_PLAYER_TRACKLET_MATCHED


def _ball_bonus(
    tracklet: Tracklet, ball_window: list[FrameDetection], geometry: CapZoneGeometry
) -> float:
    """Soft bonus for a tracklet whose head stays close to the ball (needs >= 3 detections)."""
    if len(ball_window) < MIN_BALL_DETECTIONS:
        return 0.0
    obs_by_frame = {obs.frame_index: obs for obs in tracklet.observations}
    distances = [
        _ball_head_distance(det, obs_by_frame[det.frame_index], geometry)
        for det in ball_window
        if det.frame_index in obs_by_frame
    ]
    if not distances:
        return 0.0
    return MAX_BALL_BONUS * max(0.0, 1.0 - min(distances) / BALL_PROXIMITY_RADIUS)


def _ball_head_distance(
    ball: FrameDetection, obs: TrackletObservation, geometry: CapZoneGeometry
) -> float:
    """Distance from the ball center to the head box, in units of the player box height."""
    cx, cy = _bbox_center(ball.bbox_xyxy)
    x1, y1, x2, y2 = observation_head_zone(obs, geometry)
    dx = max(x1 - cx, 0.0, cx - x2)
    dy = max(y1 - cy, 0.0, cy - y2)
    height = max(1.0, obs.bbox_xyxy[3] - obs.bbox_xyxy[1])
    return hypot(dx, dy) / height
