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
    anchor.verified = False


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


def _derive_cap_color_hint(
    candidate: Pass1EventCandidate,
    verdict: VerifierVerdict,
    profile: SportProfile,
    tracked_cap_color_hint: str | None,
    *,
    actor_changed: bool,
) -> str | None:
    """Pick the cap color hint for the final definition.

    Priority: the color the verifier observed, then the profile's expected color for the
    definition, then (same actor only) the previously observed hint. A hint left over from a
    different actor is never kept.
    """
    observed = (verdict.actor_cap_color or "").strip().lower()
    if observed:
        return observed
    expected = profile.expected_actor_cap_color(candidate.definition, tracked_cap_color_hint)
    if expected or actor_changed:
        return expected
    return candidate.cap_color_hint


def apply_event_verdict(
    candidate: Pass1EventCandidate,
    verdict: VerifierVerdict,
    catalog: Mapping[str, EventDefinition],
    profile: SportProfile,
    tracked_cap_color_hint: str | None = None,
) -> tuple[Pass1EventCandidate | None, str | None]:
    """Apply one verdict to an event candidate.

    A relabel that changes the expected actor (goalkeeper status or conceded/goal polarity, see
    :meth:`SportProfile.relabel_changes_actor`) clears ``actor_bbox_normalized`` because the old
    box points at a different player. Same-actor relabels keep the box so Pass 2 can still track
    the player. ``cap_color_hint`` is always re-derived for the final definition.

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

    actor_changed = profile.relabel_changes_actor(candidate.definition, definition)
    candidate.definition = definition
    if verdict.acting_team != ACTING_TEAM_UNKNOWN:
        candidate.acting_team = verdict.acting_team
    if actor_changed:
        candidate.actor_bbox_normalized = None
    candidate.cap_color_hint = _derive_cap_color_hint(
        candidate, verdict, profile, tracked_cap_color_hint, actor_changed=actor_changed
    )
    candidate.confidence = round(verdict.confidence, 4)
    if verdict.confidence < LOW_VERIFIER_CONFIDENCE_THRESHOLD:
        _add_reason(candidate, LOW_VERIFIER_CONFIDENCE_REASON)
    return candidate, None


def apply_anchor_verdict(
    anchor: Pass1AnchorCandidate, verdict: VerifierVerdict
) -> tuple[Pass1AnchorCandidate | None, str | None]:
    """Apply one verdict to an anchor (anchors can only be kept or dropped).

    Only an explicit ``KEEP`` verdict marks the anchor as verified. ``DROP`` removes it; any other
    verdict (for example ``RELABEL``, which anchors do not support) keeps the anchor but leaves it
    unverified and flagged for review.
    """
    if verdict.verdict == VerdictKind.DROP:
        return None, (
            f"Verifier dropped anchor {anchor.type.value} at {anchor.video_time_sec}s: "
            f"{verdict.reason or 'not confirmed'}."
        )
    if verdict.verdict != VerdictKind.KEEP:
        mark_anchor_unverified(anchor)
        return anchor, None
    anchor.verified = True
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


def prune_superseded_stoppage_starts(
    anchors: Sequence[Pass1AnchorCandidate],
) -> tuple[list[Pass1AnchorCandidate], list[str]]:
    """Remove ``StoppageStart`` anchors left unpaired before a later ``StoppageStart``.

    Reconciliation emits alternating start/end pairs, so a start directly followed by another
    start (same period, no end between) lost its end during verification. Keeping it would let it
    bridge live play up to the later stoppage's end, so it is removed.
    """
    by_period: dict[int, list[Pass1AnchorCandidate]] = defaultdict(list)
    for anchor in anchors:
        if anchor.type in (TimeAnchorType.STOPPAGE_START, TimeAnchorType.STOPPAGE_END):
            by_period[anchor.period_number].append(anchor)

    superseded: set[int] = set()
    for period_anchors in by_period.values():
        previous_start: Pass1AnchorCandidate | None = None
        for anchor in sorted(period_anchors, key=lambda a: (a.video_time_sec, a.type.value)):
            if anchor.type == TimeAnchorType.STOPPAGE_START:
                if previous_start is not None:
                    superseded.add(id(previous_start))
                previous_start = anchor
            else:
                previous_start = None

    warnings = [
        f"Removed unpaired StoppageStart at {anchor.video_time_sec}s (period "
        f"{anchor.period_number}) superseded by a later start after verification."
        for anchor in anchors
        if id(anchor) in superseded
    ]
    return [anchor for anchor in anchors if id(anchor) not in superseded], warnings


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
