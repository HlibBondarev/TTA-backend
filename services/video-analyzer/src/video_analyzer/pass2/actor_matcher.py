"""Associates a Pass 1 event candidate's spatial hint to a Pass 2 BoT-SORT tracklet."""

from __future__ import annotations

from dataclasses import dataclass
from math import hypot

from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.tracker import Tracklet, TrackletObservation, _bbox_center, _bbox_iou
from video_analyzer.schemas.request import MatchSnapshot

MIN_SPATIAL_SCORE: float = 0.15


@dataclass(frozen=True, slots=True)
class MatchedTracklet:
    """Winning tracklet and association confidence for a Pass 1 event."""

    tracklet: Tracklet
    match_score: float
    nearest_observation: TrackletObservation
    has_spatial_hint: bool = True
    is_tied: bool = False


class ActorMatcher:
    """Ranks Pass 2 player tracklets against Pass 1 spatial and team/role cues."""

    def __init__(self, min_spatial_score: float = MIN_SPATIAL_SCORE) -> None:
        self._min_spatial_score = min_spatial_score

    def match(
        self,
        candidate: Pass1EventCandidate,
        tracklets: list[Tracklet],
        snapshot: MatchSnapshot,
        frame_width: int,
        frame_height: int,
    ) -> MatchedTracklet | None:
        if not tracklets or frame_width <= 0 or frame_height <= 0:
            return None

        target_bbox_px: tuple[float, float, float, float] | None = None
        if candidate.actor_bbox_normalized is not None:
            nx1, ny1, nx2, ny2 = candidate.actor_bbox_normalized
            target_bbox_px = (
                nx1 * frame_width,
                ny1 * frame_height,
                nx2 * frame_width,
                ny2 * frame_height,
            )

        best: MatchedTracklet | None = None
        is_tied = False
        diag = hypot(frame_width, frame_height)
        is_gk_event = candidate.definition.name.lower() in {"save", "goalkeeper error"}
        cap_hint = (
            candidate.cap_color_hint or snapshot.tracked_team.cap_color_hint or ""
        ).strip().lower()
        has_spatial_hint = target_bbox_px is not None

        for tracklet in tracklets:
            obs = tracklet.observation_nearest_to(candidate.video_time_sec)
            if obs is None:
                continue

            score = self._score_candidate(
                obs=obs,
                tracklet=tracklet,
                target_bbox_px=target_bbox_px,
                diag=diag,
                cap_hint=cap_hint,
                is_gk_event=is_gk_event,
                event_time_sec=candidate.video_time_sec,
                min_spatial_score=self._min_spatial_score,
            )
            if score is None:
                continue
            if best is None or score > best.match_score:
                best = MatchedTracklet(
                    tracklet=tracklet,
                    match_score=score,
                    nearest_observation=obs,
                    has_spatial_hint=has_spatial_hint,
                )
                is_tied = False
            elif score == best.match_score:
                is_tied = True

        if best is None:
            return None
        return MatchedTracklet(
            tracklet=best.tracklet,
            match_score=best.match_score,
            nearest_observation=best.nearest_observation,
            has_spatial_hint=best.has_spatial_hint,
            is_tied=is_tied,
        )

    @staticmethod
    def _score_candidate(
        obs: TrackletObservation,
        tracklet: Tracklet,
        target_bbox_px: tuple[float, float, float, float] | None,
        diag: float,
        cap_hint: str,
        is_gk_event: bool,
        event_time_sec: float,
        min_spatial_score: float = MIN_SPATIAL_SCORE,
    ) -> float | None:
        time_penalty = min(0.25, abs(obs.video_time_sec - event_time_sec) * 0.05)
        raw_label = obs.raw_class_name.strip().lower()

        team_bonus = 0.0
        if cap_hint and cap_hint in raw_label:
            team_bonus += 0.15
        if is_gk_event and "goalkeeper" in raw_label:
            team_bonus += 0.20

        if target_bbox_px is None:
            # Without a spatial hint from Pass 1, rank by stability + role cues for review
            stability = min(0.4, len(tracklet.observations) * 0.03)
            unhinted_score = max(
                0.0,
                tracklet.mean_confidence * 0.4 + stability + team_bonus - time_penalty,
            )
            return round(unhinted_score, 4)

        iou = _bbox_iou(target_bbox_px, obs.bbox_xyxy)
        tcx, tcy = _bbox_center(target_bbox_px)
        ocx, ocy = _bbox_center(obs.bbox_xyxy)
        norm_dist = hypot(tcx - ocx, tcy - ocy) / max(1.0, diag)
        proximity = max(0.0, 1.0 - norm_dist * 4.0)
        spatial_score = iou * 0.55 + proximity * 0.35
        has_overlap = iou > 0.0
        if not has_overlap and spatial_score < min_spatial_score:
            return None

        total_score = round(
            max(0.0, min(1.0, spatial_score + team_bonus - time_penalty)),
            4,
        )
        if has_overlap:
            if total_score <= 0.0:
                return None
        elif total_score < min_spatial_score:
            return None
        return total_score
