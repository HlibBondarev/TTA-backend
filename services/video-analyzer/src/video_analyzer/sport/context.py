"""Per-job analysis context combining the DB snapshot, sport profile and runtime hints."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

from video_analyzer.schemas.request import LineupEntry, MatchSnapshot, RuntimeHints, TeamRef
from video_analyzer.sport.events import EventDefinition
from video_analyzer.sport.profile import SportProfile, resolve_sport_profile


@dataclass(frozen=True, slots=True)
class AnalysisContext:
    """Everything the pipeline needs about one match, resolved once per job."""

    snapshot: MatchSnapshot
    profile: SportProfile
    event_catalog: tuple[EventDefinition, ...]
    hints: RuntimeHints

    @property
    def tracked_team(self) -> TeamRef:
        return self.snapshot.teams.tracked

    @property
    def opponent_team(self) -> TeamRef:
        return self.snapshot.teams.opponent

    @property
    def lineup(self) -> list[LineupEntry]:
        """Tracked-team lineup (the .NET snapshot only contains the tracked team)."""
        return self.snapshot.lineup

    @property
    def tracked_side(self) -> Literal["Home", "Guest"]:
        return "Home" if self.tracked_team.id == self.snapshot.teams.home.id else "Guest"

    @property
    def opponent_side(self) -> Literal["Home", "Guest"]:
        return "Guest" if self.tracked_side == "Home" else "Home"

    @property
    def tracked_cap_color_hint(self) -> str | None:
        return self.hints.tracked_cap_color_hint


def build_analysis_context(
    snapshot: MatchSnapshot,
    hints: RuntimeHints | None = None,
    profile: SportProfile | None = None,
) -> AnalysisContext:
    """Resolve the sport profile and event catalog for a snapshot."""
    resolved = profile or resolve_sport_profile(snapshot.sport)
    return AnalysisContext(
        snapshot=snapshot,
        profile=resolved,
        event_catalog=tuple(resolved.build_event_catalog(snapshot.event_definitions)),
        hints=hints or RuntimeHints(),
    )
