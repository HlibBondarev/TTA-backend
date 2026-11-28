"""Lineup-constrained resolution with strict non-guessing confidence gating."""

from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, field
from uuid import UUID

from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.cap_resolver import CapResolutionResult
from video_analyzer.pass2.failure_reasons import (
    REVIEW_ACTOR_TEAM_UNVERIFIED,
    REVIEW_GOALKEEPER_ACTOR_ON_FIELD_ONLY_EVENT,
    REVIEW_OPPONENT_GOALKEEPER_SIDE,
    REVIEW_WEAK_ACTOR_MATCH,
)
from video_analyzer.pass2.team_classifier import TeamClass
from video_analyzer.schemas.draft import CandidateLineup
from video_analyzer.schemas.request import LineupEntry
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.sport.profile import SportProfile

_LOW_EVENT_CONFIDENCE_THRESHOLD = 0.80
_MISSING_SPATIAL_HINT_REASON = "MISSING_ACTOR_SPATIAL_HINT"
_GOALKEEPER_EVENT_NON_GOALKEEPER_REASON = "GOALKEEPER_EVENT_NON_GOALKEEPER_ACTOR"

MIN_AUTO_CONFIRM_MATCH_SCORE: float = 0.35
"""Minimum actor-to-tracklet match score required to auto-confirm a lineup entry."""

_VERIFIED_TEAM_CLASSES = frozenset({TeamClass.TRACKED, TeamClass.GOALKEEPER})
"""Team classes that may auto-confirm.

``GOALKEEPER`` is only produced together with tracked-team evidence (a tracked label or cap
color plus goalkeeper evidence), so it identifies *our* goalkeeper. Goalkeeper evidence alone
(a solid red cap or a generic goalkeeper label, which both teams share) is classified
``UNKNOWN`` and therefore blocked here with ``ACTOR_TEAM_UNVERIFIED``.
"""


@dataclass(frozen=True, slots=True)
class ActorEvidence:
    """Strength and team identity of the tracklet the cap reading came from."""

    match_score: float
    team_class: TeamClass


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
    gate_outcomes: dict[str, bool] = field(default_factory=dict)
    """Auto-confirm gate name to ``True`` when that gate blocked the match (debug only)."""


def _needs_review_outcome(
    cap_confidence: float,
    candidates: list[CandidateLineup],
    review_reasons: list[str],
    gate_outcomes: dict[str, bool] | None = None,
) -> LineupMatchOutcome:
    """Build an unresolved outcome that keeps the ranked candidates for human review."""
    return LineupMatchOutcome(
        match_lineup_id=None,
        resolved_cap_number=None,
        cap_confidence=cap_confidence,
        candidate_lineup_ids=candidates,
        needs_review=True,
        review_reasons=review_reasons,
        gate_outcomes=dict(gate_outcomes or {}),
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
        actor_evidence: ActorEvidence | None = None,
    ) -> LineupMatchOutcome:
        """Resolve a candidate to a lineup entry, or explain why it stays unresolved.

        Args:
            actor_evidence: Match score and team class of the tracked actor. When given, weak
                or team-unverified actors (and goalkeeper events on the wrong goal side) block
                auto-confirmation. They never force a pick.
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
        gate_outcomes = self._apply_gates(candidate, context, actor_evidence, review_reasons)

        resolved_num = cap_result.resolved_cap_number
        matching_entries = by_number.get(resolved_num, []) if resolved_num is not None else []

        # Collision guard: never pick arbitrarily if two lineup entries share the same cap number
        if len(matching_entries) > 1:
            _add_reason(review_reasons, "DUPLICATE_CAP_NUMBER_IN_LINEUP")
            return _needs_review_outcome(
                cap_result.confidence, ranked_candidates, review_reasons, gate_outcomes
            )

        # Strict non-guessing threshold guard
        if not matching_entries or cap_result.confidence < min_cap_confidence:
            _add_reason(review_reasons, "LOW_CAP_CONFIDENCE")
            return _needs_review_outcome(
                cap_result.confidence, ranked_candidates, review_reasons, gate_outcomes
            )

        # Unhinted, tied-track, or ambiguous-cap matches must never auto-confirm a lineup entry
        if missing_spatial_hint or actor_match_tied or cap_ambiguous or any(gate_outcomes.values()):
            return _needs_review_outcome(
                round(cap_result.confidence, 4), ranked_candidates, review_reasons, gate_outcomes
            )

        chosen = matching_entries[0]
        role_reason = self._role_mismatch_reason(candidate, chosen, context.profile)
        if role_reason is not None:
            _add_reason(review_reasons, role_reason)
            return _needs_review_outcome(
                round(cap_result.confidence, 4), ranked_candidates, review_reasons, gate_outcomes
            )

        return LineupMatchOutcome(
            match_lineup_id=chosen.id,
            resolved_cap_number=chosen.number,
            cap_confidence=round(cap_result.confidence, 4),
            candidate_lineup_ids=ranked_candidates,
            needs_review=candidate.needs_review or len(review_reasons) > 0,
            review_reasons=review_reasons,
            gate_outcomes=gate_outcomes,
        )

    @classmethod
    def _apply_gates(
        cls,
        candidate: Pass1EventCandidate,
        context: AnalysisContext,
        actor_evidence: ActorEvidence | None,
        review_reasons: list[str],
    ) -> dict[str, bool]:
        """Evaluate the gates and append the reason of every blocking gate."""
        outcomes = cls._evaluate_gates(candidate, context, actor_evidence)
        for gate_reason in (reason for reason, blocked in outcomes.items() if blocked):
            _add_reason(review_reasons, gate_reason)
        return outcomes

    @staticmethod
    def _evaluate_gates(
        candidate: Pass1EventCandidate,
        context: AnalysisContext,
        actor_evidence: ActorEvidence | None,
    ) -> dict[str, bool]:
        """Evaluate the actor-quality auto-confirm gates (``True`` means the gate blocks).

        The side check is review-only and runs without actor evidence. The score and team
        gates run only when the caller supplies evidence about the tracked actor.
        """
        outcomes: dict[str, bool] = {}
        if actor_evidence is not None:
            outcomes[REVIEW_WEAK_ACTOR_MATCH] = (
                actor_evidence.match_score < MIN_AUTO_CONFIRM_MATCH_SCORE
            )
            outcomes[REVIEW_ACTOR_TEAM_UNVERIFIED] = (
                actor_evidence.team_class not in _VERIFIED_TEAM_CLASSES
            )
        outcomes[REVIEW_OPPONENT_GOALKEEPER_SIDE] = context.profile.goalkeeper_side_violation(
            candidate.definition, candidate.tracked_attack_direction, candidate.pool_zone_hint
        )
        return outcomes

    @classmethod
    def _role_mismatch_reason(
        cls,
        candidate: Pass1EventCandidate,
        chosen: LineupEntry,
        profile: SportProfile,
    ) -> str | None:
        """Return the review reason when the event contradicts the chosen player's role.

        Review-only: the ranked candidates are kept and the actor is never reassigned.
        """
        if cls._is_goalkeeper_event_on_field_player(candidate, chosen, profile):
            return _GOALKEEPER_EVENT_NON_GOALKEEPER_REASON
        if cls._is_field_only_event_on_goalkeeper(candidate, chosen, profile):
            return REVIEW_GOALKEEPER_ACTOR_ON_FIELD_ONLY_EVENT
        return None

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

    @staticmethod
    def _is_field_only_event_on_goalkeeper(
        candidate: Pass1EventCandidate,
        chosen: LineupEntry,
        profile: SportProfile,
    ) -> bool:
        """Return True when an event a goalkeeper can never perform would be confirmed on one."""
        return profile.is_field_only_event(candidate.definition) and profile.is_goalkeeper(chosen)

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
