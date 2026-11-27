"""Lineup-constrained resolution with strict non-guessing confidence gating."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass
from uuid import UUID

from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.cap_resolver import CapResolutionResult
from video_analyzer.schemas.draft import CandidateLineup
from video_analyzer.schemas.request import LineupEntry
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.sport.profile import SportProfile

_LOW_EVENT_CONFIDENCE_THRESHOLD = 0.80
_MISSING_SPATIAL_HINT_REASON = "MISSING_ACTOR_SPATIAL_HINT"
_GOALKEEPER_EVENT_NON_GOALKEEPER_REASON = "GOALKEEPER_EVENT_NON_GOALKEEPER_ACTOR"


def _add_reason(review_reasons: list[str], reason: str) -> None:
    """Append ``reason`` unless it is already present, preserving insertion order."""
    if reason not in review_reasons:
        review_reasons.append(reason)


@dataclass(frozen=True, slots=True)
class LineupMatchOutcome:
    """Resolved lineup identity or ranked candidate suggestions for review."""

    match_lineup_id: UUID | None
    resolved_cap_number: int | None
    cap_confidence: float | None
    candidate_lineup_ids: list[CandidateLineup]
    needs_review: bool
    review_reasons: list[str]


def _needs_review_outcome(
    cap_confidence: float,
    candidates: list[CandidateLineup],
    review_reasons: list[str],
) -> LineupMatchOutcome:
    """Build an unresolved outcome that keeps the ranked candidates for human review."""
    return LineupMatchOutcome(
        match_lineup_id=None,
        resolved_cap_number=None,
        cap_confidence=cap_confidence,
        candidate_lineup_ids=candidates,
        needs_review=True,
        review_reasons=review_reasons,
    )


class LineupMatcher:
    """Resolves cap predictions to matchLineupId strictly within the tracked team's lineup."""

    def resolve(
        self,
        candidate: Pass1EventCandidate,
        cap_result: CapResolutionResult | None,
        context: AnalysisContext,
        min_cap_confidence: float,
        max_candidates: int = 5,
        has_spatial_hint: bool = True,
        actor_match_tied: bool = False,
        cap_ambiguous: bool = False,
        pass2_failure_reason: str | None = None,
    ) -> LineupMatchOutcome:
        """Resolve a candidate to a lineup entry, or explain why it stays unresolved.

        Args:
            pass2_failure_reason: Specific reason Pass 2 produced no cap reading (see
                ``Pass2FailureReason``). When omitted and ``cap_result`` is None, the legacy
                ``NO_PLAYER_TRACKLET_MATCHED`` reason is used.
        """
        team_lineup = list(context.lineup)
        by_number: dict[int, list[LineupEntry]] = defaultdict(list)
        for entry in team_lineup:
            by_number[entry.number].append(entry)

        missing_spatial_hint = (not has_spatial_hint) or (candidate.actor_bbox_normalized is None)
        review_reasons = self._initial_review_reasons(candidate, actor_match_tied, cap_ambiguous)

        if cap_result is None:
            return self._no_cap_outcome(
                candidate,
                context,
                review_reasons,
                missing_spatial_hint,
                max_candidates,
                pass2_failure_reason,
            )

        ranked_candidates = self._build_ranked_candidates(
            cap_result=cap_result,
            by_number=by_number,
            team_lineup=team_lineup,
            candidate=candidate,
            max_candidates=max_candidates,
            profile=context.profile,
        )
        if missing_spatial_hint:
            _add_reason(review_reasons, _MISSING_SPATIAL_HINT_REASON)

        resolved_num = cap_result.resolved_cap_number
        matching_entries = by_number.get(resolved_num, []) if resolved_num is not None else []

        # Collision guard: never pick arbitrarily if two lineup entries share the same cap number
        if len(matching_entries) > 1:
            _add_reason(review_reasons, "DUPLICATE_CAP_NUMBER_IN_LINEUP")
            return _needs_review_outcome(cap_result.confidence, ranked_candidates, review_reasons)

        # Strict non-guessing threshold guard
        if not matching_entries or cap_result.confidence < min_cap_confidence:
            _add_reason(review_reasons, "LOW_CAP_CONFIDENCE")
            return _needs_review_outcome(cap_result.confidence, ranked_candidates, review_reasons)

        # Unhinted, tied-track, or ambiguous-cap matches must never auto-confirm a lineup entry
        if missing_spatial_hint or actor_match_tied or cap_ambiguous:
            return _needs_review_outcome(
                round(cap_result.confidence, 4), ranked_candidates, review_reasons
            )

        chosen = matching_entries[0]
        if self._is_goalkeeper_event_on_field_player(candidate, chosen, context.profile):
            _add_reason(review_reasons, _GOALKEEPER_EVENT_NON_GOALKEEPER_REASON)
            return _needs_review_outcome(
                round(cap_result.confidence, 4), ranked_candidates, review_reasons
            )

        return LineupMatchOutcome(
            match_lineup_id=chosen.id,
            resolved_cap_number=chosen.number,
            cap_confidence=round(cap_result.confidence, 4),
            candidate_lineup_ids=ranked_candidates,
            needs_review=candidate.needs_review or len(review_reasons) > 0,
            review_reasons=review_reasons,
        )

    @staticmethod
    def _is_goalkeeper_event_on_field_player(
        candidate: Pass1EventCandidate,
        chosen: LineupEntry,
        profile: SportProfile,
    ) -> bool:
        """Return True when a goalkeeper-only event would be confirmed on a field player."""
        return profile.is_goalkeeper_event(candidate.definition) and not profile.is_goalkeeper(
            chosen
        )

    def _no_cap_outcome(
        self,
        candidate: Pass1EventCandidate,
        context: AnalysisContext,
        review_reasons: list[str],
        missing_spatial_hint: bool,
        max_candidates: int,
        pass2_failure_reason: str | None,
    ) -> LineupMatchOutcome:
        """Build the unresolved outcome used when Pass 2 produced no cap reading."""
        _add_reason(review_reasons, pass2_failure_reason or "NO_PLAYER_TRACKLET_MATCHED")
        if missing_spatial_hint:
            _add_reason(review_reasons, _MISSING_SPATIAL_HINT_REASON)
        return LineupMatchOutcome(
            match_lineup_id=None,
            resolved_cap_number=None,
            cap_confidence=None,
            candidate_lineup_ids=self._fallback_candidates(
                candidate, list(context.lineup), max_candidates, context.profile
            ),
            needs_review=True,
            review_reasons=review_reasons,
        )

    @staticmethod
    def _initial_review_reasons(
        candidate: Pass1EventCandidate,
        actor_match_tied: bool,
        cap_ambiguous: bool,
    ) -> list[str]:
        """Seed review reasons from Pass 1 plus the Pass 2 tracking/ambiguity signals."""
        review_reasons: list[str] = list(dict.fromkeys(candidate.review_reasons))
        if candidate.confidence < _LOW_EVENT_CONFIDENCE_THRESHOLD:
            _add_reason(review_reasons, "LOW_EVENT_CONFIDENCE")
        if actor_match_tied:
            _add_reason(review_reasons, "TIED_ACTOR_TRACKLET_MATCH")
        if cap_ambiguous:
            _add_reason(review_reasons, "AMBIGUOUS_CAP_DETECTION")
        return review_reasons

    @staticmethod
    def _build_ranked_candidates(
        cap_result: CapResolutionResult,
        by_number: dict[int, list[LineupEntry]],
        team_lineup: list[LineupEntry],
        candidate: Pass1EventCandidate,
        max_candidates: int,
        profile: SportProfile,
    ) -> list[CandidateLineup]:
        scored: list[CandidateLineup] = []
        seen_ids: set[UUID] = set()

        for num, score in sorted(
            cap_result.candidate_scores.items(), key=lambda kv: kv[1], reverse=True
        ):
            for entry in by_number.get(num, []):
                scored.append(
                    CandidateLineup(
                        match_lineup_id=entry.id,
                        cap_number=entry.number,
                        score=round(max(0.0, min(1.0, score)), 4),
                        reason=cap_result.reasoning or f"Matched cap #{entry.number}",
                    )
                )
                seen_ids.add(entry.id)

        if not scored:
            return LineupMatcher._fallback_candidates(
                candidate, team_lineup, max_candidates, profile
            )

        return scored[:max_candidates]

    @staticmethod
    def _fallback_candidates(
        candidate: Pass1EventCandidate,
        team_lineup: list[LineupEntry],
        max_candidates: int,
        profile: SportProfile,
    ) -> list[CandidateLineup]:
        if profile.is_goalkeeper_event(candidate.definition):
            gks = [e for e in team_lineup if profile.is_goalkeeper(e)]
            others = [e for e in team_lineup if not profile.is_goalkeeper(e)]
            ordered = gks + others
            return [
                CandidateLineup(
                    match_lineup_id=e.id,
                    cap_number=e.number,
                    score=0.45 if profile.is_goalkeeper(e) else 0.1,
                    reason=(
                        "Goalkeeper role heuristic"
                        if profile.is_goalkeeper(e)
                        else "Roster fallback"
                    ),
                )
                for e in ordered[:max_candidates]
            ]

        return [
            CandidateLineup(
                match_lineup_id=e.id,
                cap_number=e.number,
                score=0.1,
                reason="Team lineup candidate",
            )
            for e in team_lineup[:max_candidates]
        ]
