"""Prompt builder for Pass 1 Water Polo VLM analysis."""

from __future__ import annotations

from decimal import Decimal

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.schemas.request import MatchSnapshot


class Pass1PromptBuilder:
    """Constructs deterministic Pass 1 prompts anchored to exact TTA promptCodes."""

    def build_chunk_prompt(self, snapshot: MatchSnapshot, chunk: VideoChunk) -> str:
        """Build the Pass 1 instruction prompt for a single overlapping chunk."""
        cfg = snapshot.sport_configuration
        tracked = snapshot.tracked_team
        opponent = snapshot.opponent_team

        tracked_caps = tracked.cap_color_hint or "unknown"
        opponent_caps = opponent.cap_color_hint or "unknown"
        chunk_duration = max(
            Decimal("0.0"),
            Decimal(str(chunk.end_sec)) - Decimal(str(chunk.start_sec)),
        )
        chunk_duration_str = f"{chunk_duration:f}"

        definitions_lines = [
            (
                f"- `{d.prompt_code}` | name={d.name} | short={d.short_name} "
                f"| polarity={'POSITIVE' if d.is_positive else 'NEGATIVE'} "
                f"| isShot={str(d.is_shot).lower()} | score={d.score} "
                f"| foulImpact={d.foul_impact}"
                + (f" ({d.penalty_duration_seconds}s)" if d.penalty_duration_seconds else "")
            )
            for d in snapshot.event_definitions
        ]
        definitions_block = "\n".join(definitions_lines)

        return (
            f"You are an expert {snapshot.sport.name} video and audio analyst.\n"
            "Analyze this video chunk and detect ONLY events attributable to the TRACKED TEAM "
            "(plus negative events conceded by the tracked team, such as Goal Conceded / GC) "
            "and match timeline anchors.\n\n"
            "### Match Configuration\n"
            f"- Sport: {snapshot.sport.name} ({snapshot.sport.short_name})\n"
            f"- Periods: {cfg.periods_count} periods x {cfg.period_duration_minutes} minutes "
            f"(usesCleanTime={str(cfg.uses_clean_time).lower()})\n"
            f"- Active players per team in water: {cfg.active_players_limit}\n"
            f"- Tracked Team ({tracked.side}): {tracked.name} (cap color hint: {tracked_caps})\n"
            f"- Opponent Team ({opponent.side}): {opponent.name} "
            f"(cap color hint: {opponent_caps})\n"
            f"- Chunk Timeline Window: {chunk.start_sec:.2f}s to {chunk.end_sec:.2f}s "
            f"(duration {chunk_duration_str}s)\n\n"
            "### Critical Rules\n"
            "1. ALWAYS return `time_sec` relative to the START of THIS chunk (0.0 to "
            f"{chunk_duration_str}).\n"
            "2. ALWAYS identify actions using the exact `promptCode` from the catalog below. "
            "NEVER invent codes or confuse positive vs negative definitions with the same name "
            "(for example, positive Goal vs negative Goal Conceded).\n"
            "3. Use BOTH visual action AND audio cues (referee whistles, period horn/buzzer, "
            "goal whistle, shot-clock horn) to pinpoint timestamps.\n"
            "4. For `actor_bbox_normalized`, provide `[x1, y1, x2, y2]` in `[0.0, 1.0]` around "
            "the acting player of the tracked team when visible; otherwise null.\n"
            "5. Detect `time_anchors` using types `PeriodStart` (sprint swim-off whistle), "
            "`PeriodEnd` (end-of-period buzzer/horn), `StoppageStart` (timeout/goal/review pause), "
            "and `StoppageEnd` (play restart whistle).\n\n"
            "### Event Definition Catalog (`promptCode`)\n"
            f"{definitions_block}\n"
        )
