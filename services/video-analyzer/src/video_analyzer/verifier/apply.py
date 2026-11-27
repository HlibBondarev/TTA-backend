"""Pure helpers applying verifier verdicts (or fail-open markers) to Pass 1 candidates."""

from __future__ import annotations

from collections import defaultdict
from collections.abc import Mapping, Sequence

from video_analyzer.pass1.models import (
    ACTING_TEAM_OPPONENT,
    ACTING_TEAM_UNKNOWN,
    LabelConflictGroup,
    Pass1AnchorCandidate,
    Pass1EventCandidate,
)
from video_analyzer.schemas.draft import TimeAnchorType
from video_analyzer.sport.events import EventDefinition
from video_analyzer.sport.profile import SportProfile
from video_analyzer.verifier.models import (
    LOW_VERIFIER_CONFIDENCE_REASON,
    LOW_VERIFIER_CONFIDENCE_THRESHOLD,
    RELABEL_REJECTED_REASON,
    UNVERIFIED_REASON,
    VerdictKind,
    VerifierVerdict,
)


def _add_reason(candidate: Pass1EventCandidate, reason: str) -> None:
    candidate.needs_review = True
    if reason not in candidate.review_reasons:
        candidate.review_reasons.append(reason)


def mark_event_unverified(candidate: Pass1EventCandidate) -> None:
    """Fail open: keep the candidate but flag it for review as ``UNVERIFIED``."""
    _add_reason(candidate, UNVERIFIED_REASON)


def mark_anchor_unverified(anchor: Pass1AnchorCandidate) -> None:
    """Fail open: keep the anchor but flag it for review."""
    anchor.needs_review = True


def _resolve_relabel(
    candidate: Pass1EventCandidate,
    verdict: VerifierVerdict,
    catalog: Mapping[str, EventDefinition],
    profile: SportProfile,
) -> EventDefinition:
    """Return the relabel target when it is an allowed confusable alternative, else the label."""
    current = candidate.definition
    if verdict.verdict != VerdictKind.RELABEL:
        return current
    target = catalog.get(verdict.relabel_prompt_code or "")
    if target is None or not profile.are_confusable(current, target):
        _add_reason(candidate, RELABEL_REJECTED_REASON)
        return current
    return target


def apply_event_verdict(
    candidate: Pass1EventCandidate,
    verdict: VerifierVerdict,
    catalog: Mapping[str, EventDefinition],
    profile: SportProfile,
) -> tuple[Pass1EventCandidate | None, str | None]:
    """Apply one verdict to an event candidate.

    Returns:
        The updated candidate (mutated in place) or ``None`` when it must be dropped, plus a
        warning describing a drop.
    """
    label = f"'{candidate.definition.short_name}' at {candidate.video_time_sec}s"
    if verdict.verdict == VerdictKind.DROP:
        return None, f"Verifier dropped event {label}: {verdict.reason or 'not confirmed'}."

    definition = _resolve_relabel(candidate, verdict, catalog, profile)
    if verdict.acting_team == ACTING_TEAM_OPPONENT and not profile.is_conceded_event(definition):
        return None, f"Verifier dropped opponent-team event {label}."
    if not verdict.is_live_play:
        return None, f"Verifier dropped event {label}: not live play."

    candidate.definition = definition
    if verdict.acting_team != ACTING_TEAM_UNKNOWN:
        candidate.acting_team = verdict.acting_team
    if verdict.actor_cap_color and verdict.actor_cap_color.strip():
        candidate.cap_color_hint = verdict.actor_cap_color.strip().lower()
    candidate.confidence = round(verdict.confidence, 4)
    if verdict.confidence < LOW_VERIFIER_CONFIDENCE_THRESHOLD:
        _add_reason(candidate, LOW_VERIFIER_CONFIDENCE_REASON)
    return candidate, None


def apply_anchor_verdict(
    anchor: Pass1AnchorCandidate, verdict: VerifierVerdict
) -> tuple[Pass1AnchorCandidate | None, str | None]:
    """Apply one verdict to an anchor (anchors can only be kept or dropped)."""
    if verdict.verdict == VerdictKind.DROP:
        return None, (
            f"Verifier dropped anchor {anchor.type.value} at {anchor.video_time_sec}s: "
            f"{verdict.reason or 'not confirmed'}."
        )
    anchor.confidence = round(verdict.confidence, 4)
    if verdict.confidence < LOW_VERIFIER_CONFIDENCE_THRESHOLD:
        anchor.needs_review = True
    return anchor, None


def prune_orphan_stoppage_ends(
    anchors: Sequence[Pass1AnchorCandidate],
) -> tuple[list[Pass1AnchorCandidate], list[str]]:
    """Remove ``StoppageEnd`` anchors left without a preceding ``StoppageStart`` in the period.

    Dropping a stoppage start during verification must not leave a dangling end behind.
    """
    by_period: dict[int, list[Pass1AnchorCandidate]] = defaultdict(list)
    for anchor in anchors:
        by_period[anchor.period_number].append(anchor)

    orphans: set[int] = set()
    for period_anchors in by_period.values():
        open_start = False
        for anchor in sorted(period_anchors, key=lambda a: (a.video_time_sec, a.type.value)):
            if anchor.type == TimeAnchorType.STOPPAGE_START:
                open_start = True
            elif anchor.type == TimeAnchorType.STOPPAGE_END:
                if not open_start:
                    orphans.add(id(anchor))
                open_start = False

    warnings = [
        f"Removed orphan StoppageEnd at {anchor.video_time_sec}s (period "
        f"{anchor.period_number}) after verification."
        for anchor in anchors
        if id(anchor) in orphans
    ]
    return [anchor for anchor in anchors if id(anchor) not in orphans], warnings


def settle_conflict_groups(
    events: Sequence[Pass1EventCandidate],
    groups: Sequence[LabelConflictGroup],
) -> tuple[list[Pass1EventCandidate], list[str]]:
    """Keep one candidate per label inside each cross-chunk conflict group after verification.

    Verification can relabel two members to the same definition; the lower-confidence duplicate
    is then removed. Members with distinct labels are left to the verifier's own decisions.
    """
    survivors = {id(event) for event in events}
    warnings: list[str] = []
    for group in groups:
        best_by_definition: dict[object, Pass1EventCandidate] = {}
        for member in group.candidates:
            if id(member) not in survivors:
                continue
            key = member.definition.event_definition_id
            current = best_by_definition.get(key)
            if current is None:
                best_by_definition[key] = member
                continue
            winner, loser = (
                (member, current) if member.confidence > current.confidence else (current, member)
            )
            best_by_definition[key] = winner
            survivors.discard(id(loser))
            warnings.append(
                f"Removed duplicate '{loser.definition.short_name}' at {loser.video_time_sec}s "
                "after settling a cross-chunk label conflict."
            )
    return [event for event in events if id(event) in survivors], warnings
