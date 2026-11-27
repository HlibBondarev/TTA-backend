"""Prompt builder for Pass 1 VLM analysis (sport wording comes from the SportProfile)."""

from __future__ import annotations

from collections.abc import Sequence
from decimal import Decimal

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.sport.events import EventDefinition
from video_analyzer.sport.profile import PromptRuleBlock


def _format_definition_line(definition: EventDefinition) -> str:
    penalty = (
        f" ({definition.penalty_duration_seconds}s)" if definition.penalty_duration_seconds else ""
    )
    return (
        f"- `{definition.prompt_code}` | name={definition.name} | short={definition.short_name} "
        f"| polarity={'POSITIVE' if definition.is_positive else 'NEGATIVE'} "
        f"| isShot={str(definition.is_shot).lower()} | score={definition.score} "
        f"| foulImpact={definition.foul_impact}{penalty}"
    )


def _polarity_example(catalog: Sequence[EventDefinition]) -> str:
    """Build an example pair of same-name definitions with opposite polarity (or empty text)."""
    by_name: dict[str, dict[bool, EventDefinition]] = {}
    for definition in catalog:
        by_name.setdefault(definition.name.strip().casefold(), {}).setdefault(
            definition.is_positive, definition
        )
    for variants in by_name.values():
        if True in variants and False in variants:
            positive, negative = variants[True], variants[False]
            return (
                f" (for example `{positive.prompt_code}` ({positive.name}, POSITIVE) vs "
                f"`{negative.prompt_code}` ({negative.name}, NEGATIVE))"
            )
    return ""


def _format_rule_blocks(blocks: Sequence[PromptRuleBlock]) -> str:
    sections = [block.format() for block in blocks]
    return "\n\n".join(sections) + "\n\n" if sections else ""


class Pass1PromptBuilder:
    """Constructs deterministic Pass 1 prompts anchored to exact internal promptCodes.

    Only the event catalog, team names, runtime cap-color hints and scalar rule values are
    interpolated. Raw SVG (``playground`` / ``shotMap``) is deliberately never read here.
    """

    def build_chunk_prompt(self, context: AnalysisContext, chunk: VideoChunk) -> str:
        """Build the Pass 1 instruction prompt for a single overlapping chunk."""
        snapshot = context.snapshot
        cfg = snapshot.sport_configuration
        text = context.profile.pass1_text
        tracked = context.tracked_team
        opponent = context.opponent_team

        tracked_caps = context.hints.tracked_cap_color_hint or "unknown"
        opponent_caps = context.hints.opponent_cap_color_hint or "unknown"
        chunk_duration = max(
            Decimal("0.0"),
            Decimal(str(chunk.end_sec)) - Decimal(str(chunk.start_sec)),
        )
        chunk_duration_str = f"{chunk_duration:f}"
        definitions_block = "\n".join(_format_definition_line(d) for d in context.event_catalog)

        return (
            f"You are an expert {snapshot.sport.name} video and audio analyst.\n"
            "Analyze this video chunk and detect ONLY events attributable to the TRACKED TEAM "
            f"{text.conceded_events_clause}"
            "and match timeline anchors.\n\n"
            "### Match Configuration\n"
            f"- Sport: {snapshot.sport.name} ({snapshot.sport.short_name})\n"
            f"- Periods: {cfg.periods_count} periods x {cfg.period_duration_minutes} minutes "
            f"(usesCleanTime={str(cfg.uses_clean_time).lower()})\n"
            f"- {text.active_players_label}: {cfg.active_players_limit}\n"
            f"- Tracked Team ({context.tracked_side}): {tracked.name} "
            f"(cap color hint: {tracked_caps})\n"
            f"- Opponent Team ({context.opponent_side}): {opponent.name} "
            f"(cap color hint: {opponent_caps})\n"
            f"- Clip duration: {chunk_duration_str}s\n\n"
            "### Critical Rules\n"
            "1. ALWAYS return `time_sec` in seconds relative to the START of THIS clip "
            f"(0.0 is the first frame, valid range 0.0 to {chunk_duration_str}). NEVER use "
            "the time in the full match video.\n"
            "2. ALWAYS identify actions using the exact `promptCode` from the catalog below. "
            "NEVER invent codes or confuse positive vs negative definitions with the same name"
            f"{_polarity_example(context.event_catalog)}.\n"
            f"3. Use BOTH visual action AND audio cues {text.audio_cues} to pinpoint "
            "timestamps.\n"
            "4. `actor_bbox_normalized` is REQUIRED for every event whenever the acting "
            "player is visible in the frame at the event moment: provide `[x1, y1, x2, y2]` as "
            "fractions of the frame width/height in `[0.0, 1.0]` (not pixels, not 0-1000) with "
            "x1 < x2 and y1 < y2, around the acting player. Use null only when the actor is "
            "truly not visible.\n"
            f"5. Detect `time_anchors` using types `PeriodStart` ({text.period_start_cue}), "
            f"`PeriodEnd` ({text.period_end_cue}), `StoppageStart` ({text.stoppage_start_cue}), "
            f"and `StoppageEnd` ({text.stoppage_end_cue}).\n"
            "6. Set `acting_team` for every event to `tracked`, `opponent` or `unknown`, and "
            "`tracked_attack_direction` once per clip (`left`, `right` or `unknown`).\n\n"
            f"{_format_rule_blocks(context.profile.pass1_rules)}"
            "### Event Definition Catalog (`promptCode`)\n"
            f"{definitions_block}\n"
        )
