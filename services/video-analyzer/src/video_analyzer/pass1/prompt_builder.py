"""Prompt builder for Pass 1 VLM analysis (sport wording comes from the SportProfile)."""

from __future__ import annotations

from decimal import Decimal

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.sport.context import AnalysisContext
from video_analyzer.sport.events import EventDefinition


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
            f"- Chunk Timeline Window: {chunk.start_sec:.2f}s to {chunk.end_sec:.2f}s "
            f"(duration {chunk_duration_str}s)\n\n"
            "### Critical Rules\n"
            "1. ALWAYS return `time_sec` relative to the START of THIS chunk (0.0 to "
            f"{chunk_duration_str}).\n"
            "2. ALWAYS identify actions using the exact `promptCode` from the catalog below. "
            "NEVER invent codes or confuse positive vs negative definitions with the same name "
            "(for example, positive Goal vs negative Goal Conceded).\n"
            f"3. Use BOTH visual action AND audio cues {text.audio_cues} to pinpoint "
            "timestamps.\n"
            "4. For `actor_bbox_normalized`, provide `[x1, y1, x2, y2]` in `[0.0, 1.0]` around "
            "the acting player of the tracked team when visible; otherwise null.\n"
            f"5. Detect `time_anchors` using types `PeriodStart` ({text.period_start_cue}), "
            f"`PeriodEnd` ({text.period_end_cue}), `StoppageStart` ({text.stoppage_start_cue}), "
            f"and `StoppageEnd` ({text.stoppage_end_cue}).\n\n"
            "### Event Definition Catalog (`promptCode`)\n"
            f"{definitions_block}\n"
        )
