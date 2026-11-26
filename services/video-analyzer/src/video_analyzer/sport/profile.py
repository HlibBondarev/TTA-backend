"""Sport profiles isolating sport-specific vocabulary and role logic from the core pipeline."""

from __future__ import annotations

from abc import ABC, abstractmethod
from collections.abc import Sequence
from dataclasses import dataclass

from video_analyzer.schemas.request import EventDefinitionSnapshot, LineupEntry, SportSnapshot
from video_analyzer.sport.events import EventDefinition, build_event_catalog


@dataclass(frozen=True, slots=True)
class Pass1PromptText:
    """Sport-specific wording fragments consumed by the Pass 1 prompt builder."""

    conceded_events_clause: str
    active_players_label: str
    audio_cues: str
    period_start_cue: str
    period_end_cue: str
    stoppage_start_cue: str
    stoppage_end_cue: str


class SportProfile(ABC):
    """Sport-specific behavior consumed by an otherwise sport-agnostic pipeline."""

    @property
    @abstractmethod
    def key(self) -> str:
        """Stable profile identifier, e.g. ``water_polo``."""

    @property
    @abstractmethod
    def pass1_text(self) -> Pass1PromptText:
        """Wording fragments for the Pass 1 prompt."""

    @property
    def goalkeeper_label_token(self) -> str | None:
        """Lower-case detector class substring that marks a goalkeeper, if the sport has one."""
        return None

    def is_goalkeeper(self, entry: LineupEntry) -> bool:
        """Return True when the lineup entry plays the goalkeeper position."""
        return False

    def is_goalkeeper_event(self, definition: EventDefinition) -> bool:
        """Return True when the event is normally performed by the goalkeeper."""
        return False

    def build_event_catalog(
        self, definitions: Sequence[EventDefinitionSnapshot]
    ) -> list[EventDefinition]:
        """Build the internal event catalog with deterministic prompt codes."""
        return build_event_catalog(definitions)


class DefaultProfile(SportProfile):
    """Neutral fallback for sports without a dedicated profile."""

    _TEXT = Pass1PromptText(
        conceded_events_clause="(plus negative events conceded by the tracked team) ",
        active_players_label="Active players per team on the field",
        audio_cues="(referee whistles, period horn/buzzer)",
        period_start_cue="period start whistle",
        period_end_cue="end-of-period buzzer/horn",
        stoppage_start_cue="timeout/score/review pause",
        stoppage_end_cue="play restart whistle",
    )

    @property
    def key(self) -> str:
        return "default"

    @property
    def pass1_text(self) -> Pass1PromptText:
        return self._TEXT


class WaterPoloProfile(SportProfile):
    """Water polo conventions preserved verbatim from the pre-profile baseline."""

    _GOALKEEPER_POSITION_NAME = "goalkeeper"
    _GOALKEEPER_POSITION_SHORT = "gk"
    _GOALKEEPER_EVENT_NAMES = frozenset({"save", "goalkeeper error"})
    _TEXT = Pass1PromptText(
        conceded_events_clause=(
            "(plus negative events conceded by the tracked team, such as Goal Conceded / GC) "
        ),
        active_players_label="Active players per team in water",
        audio_cues="(referee whistles, period horn/buzzer, goal whistle, shot-clock horn)",
        period_start_cue="sprint swim-off whistle",
        period_end_cue="end-of-period buzzer/horn",
        stoppage_start_cue="timeout/goal/review pause",
        stoppage_end_cue="play restart whistle",
    )

    @property
    def key(self) -> str:
        return "water_polo"

    @property
    def pass1_text(self) -> Pass1PromptText:
        return self._TEXT

    @property
    def goalkeeper_label_token(self) -> str | None:
        return "goalkeeper"

    def is_goalkeeper(self, entry: LineupEntry) -> bool:
        name = entry.position_name.strip().casefold()
        short = entry.position_short_name.strip().casefold()
        return name == self._GOALKEEPER_POSITION_NAME or short == self._GOALKEEPER_POSITION_SHORT

    def is_goalkeeper_event(self, definition: EventDefinition) -> bool:
        return definition.name.strip().casefold() in self._GOALKEEPER_EVENT_NAMES


_WATER_POLO_IDENTIFIERS = frozenset({"wp", "waterpolo", "water polo", "water-polo"})


def resolve_sport_profile(sport: SportSnapshot) -> SportProfile:
    """Resolve the profile for a snapshot sport, falling back to :class:`DefaultProfile`."""
    identifiers = {sport.short_name.strip().casefold(), sport.name.strip().casefold()}
    if identifiers & _WATER_POLO_IDENTIFIERS:
        return WaterPoloProfile()
    return DefaultProfile()
