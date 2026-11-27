"""Prompt and response schema for the clip-based Pass 1 verifier."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass
from typing import Any, get_args

from video_analyzer.pass1.models import (
    ATTACK_DIRECTION_UNKNOWN,
    ActingTeam,
    AttackDirection,
)
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.sport.events import EventDefinition
from video_analyzer.verifier.models import VerdictKind


@dataclass(frozen=True, slots=True)
class ClipDescriptor:
    """Everything the prompt says about one clip of a verification batch."""

    item_id: int
    is_anchor: bool
    label: str
    period_number: int
    candidate_offset_sec: float
    clip_duration_sec: float
    alternatives: tuple[EventDefinition, ...] = ()
    competing_labels: tuple[str, ...] = ()
    tracked_attack_direction: AttackDirection = ATTACK_DIRECTION_UNKNOWN


def build_verifier_response_schema(types_module: Any) -> Any:
    """Build a default-free Gemini response schema for one verification batch."""
    verdict_schema = types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "item_id": types_module.Schema(type=types_module.Type.INTEGER),
            "verdict": types_module.Schema(
                type=types_module.Type.STRING,
                enum=[kind.value for kind in VerdictKind],
            ),
            "relabel_prompt_code": types_module.Schema(
                type=types_module.Type.STRING,
                nullable=True,
            ),
            "acting_team": types_module.Schema(
                type=types_module.Type.STRING,
                enum=list(get_args(ActingTeam)),
            ),
            "is_live_play": types_module.Schema(type=types_module.Type.BOOLEAN),
            "actor_cap_color": types_module.Schema(
                type=types_module.Type.STRING,
                nullable=True,
            ),
            "confidence": types_module.Schema(type=types_module.Type.NUMBER),
            "reason": types_module.Schema(type=types_module.Type.STRING, nullable=True),
        },
        required=["item_id", "verdict", "acting_team", "is_live_play", "confidence"],
    )
    return types_module.Schema(
        type=types_module.Type.OBJECT,
        properties={
            "verdicts": types_module.Schema(
                type=types_module.Type.ARRAY,
                items=verdict_schema,
            )
        },
        required=["verdicts"],
    )


def build_clip_header(clip: ClipDescriptor) -> str:
    """Text placed right before a clip's video part so the model can match verdicts to clips."""
    kind = "TIME ANCHOR" if clip.is_anchor else "EVENT"
    lines = [
        f"Clip {clip.item_id} ({clip.clip_duration_sec:.1f}s long): candidate {kind} "
        f"{clip.label}, period {clip.period_number}, claimed at "
        f"{clip.candidate_offset_sec:.1f}s into this clip."
    ]
    if clip.alternatives:
        options = ", ".join(
            f"`{alt.prompt_code}` ({alt.name}, {'POSITIVE' if alt.is_positive else 'NEGATIVE'})"
            for alt in clip.alternatives
        )
        lines.append(f"Allowed relabel targets: {options}.")
    elif not clip.is_anchor:
        lines.append("No relabel targets are allowed for this clip (use keep or drop).")
    if clip.tracked_attack_direction != ATTACK_DIRECTION_UNKNOWN:
        attack = clip.tracked_attack_direction
        defend = "right" if attack == "left" else "left"
        lines.append(
            f"Pass 1 reported that the tracked team attacks the {attack.upper()} goal in this "
            f"part of the match, so it defends the {defend.upper()} goal."
        )
    if clip.competing_labels:
        lines.append(
            "Another analysis window reported a competing label for the same moment: "
            + ", ".join(clip.competing_labels)
            + ". Decide which label (if any) is correct."
        )
    return " ".join(lines)


def _format_sport_rules(context: AnalysisContext) -> str:
    blocks = context.profile.verifier_rules
    return "\n\n".join(block.format() for block in blocks) + "\n" if blocks else ""


def build_verifier_prompt(context: AnalysisContext, clips: Sequence[ClipDescriptor]) -> str:
    """Build the batch instruction prompt (clips are attached as separate video parts)."""
    snapshot = context.snapshot
    hints = context.hints
    goalkeeper_color = context.profile.goalkeeper_cap_color
    goalkeeper_line = (
        f"- Goalkeepers wear {goalkeeper_color} caps regardless of their team.\n"
        if goalkeeper_color
        else ""
    )
    return (
        f"You are an expert {snapshot.sport.name} video and audio referee verifying "
        f"{len(clips)} short clip(s) cut around candidate detections.\n\n"
        "### Match Context\n"
        f"- Tracked team: {context.tracked_team.name} "
        f"(cap color hint: {hints.tracked_cap_color_hint or 'unknown'})\n"
        f"- Opponent team: {context.opponent_team.name} "
        f"(cap color hint: {hints.opponent_cap_color_hint or 'unknown'})\n"
        f"{goalkeeper_line}\n"
        "### Instructions\n"
        "1. Judge every clip independently and return exactly one verdict per clip, using the "
        "clip's `item_id`.\n"
        "2. `keep`: the candidate happened at the claimed moment, attributed to the tracked "
        "team (or a conceded event). `drop`: it did not happen, belongs to the opponent, or is "
        "a duplicate. `relabel`: the action happened but the label is wrong; set "
        "`relabel_prompt_code` to one of the clip's allowed relabel targets.\n"
        "3. Time anchors can only be `keep` or `drop`.\n"
        "4. `acting_team`: `tracked`, `opponent` or `unknown` for the acting player.\n"
        "5. `is_live_play`: false when play is stopped at the claimed moment (timeout, goal "
        "restart, injury, review, between periods).\n"
        "6. `actor_cap_color`: the cap color of the acting player when visible, otherwise null.\n"
        "7. `confidence`: your certainty in the verdict between 0.0 and 1.0.\n"
        "8. Use BOTH the video and the audio (whistles, horns) of each clip.\n\n"
        f"{_format_sport_rules(context)}"
    )
