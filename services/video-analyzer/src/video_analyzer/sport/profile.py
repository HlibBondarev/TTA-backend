"""Sport profiles isolating sport-specific vocabulary and role logic from the core pipeline."""

from __future__ import annotations

from abc import ABC, abstractmethod
from collections.abc import Sequence
from dataclasses import dataclass

from video_analyzer.schemas.request import EventDefinitionSnapshot, LineupEntry, SportSnapshot
from video_analyzer.sport.events import EventDefinition, build_event_catalog

DefinitionKey = tuple[str, bool]
"""``(casefolded event name, is_positive)`` identity of an event definition within a sport."""


def definition_key(definition: EventDefinition) -> DefinitionKey:
    """Return the sport-level identity key of an event definition."""
    return (definition.name.strip().casefold(), definition.is_positive)


@dataclass(frozen=True, slots=True)
class PromptRuleBlock:
    """A titled group of sport-specific rules rendered into the Pass 1 prompt."""

    title: str
    rules: tuple[str, ...]

    def format(self) -> str:
        """Render the block as a markdown section for a prompt."""
        return f"### {self.title}\n" + "\n".join(f"- {rule}" for rule in self.rules)


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

    @property
    def goalkeeper_cap_color(self) -> str | None:
        """Cap color worn by goalkeepers regardless of team, if the sport mandates one."""
        return None

    @property
    def goalkeeper_event_keys(self) -> frozenset[DefinitionKey]:
        """``(name, is_positive)`` keys of events normally performed by the goalkeeper."""
        return frozenset()

    @property
    def conceded_event_keys(self) -> frozenset[DefinitionKey]:
        """Keys of events attributed to the tracked team although the opponent acts first."""
        return frozenset()

    @property
    def pass1_rules(self) -> tuple[PromptRuleBlock, ...]:
        """Sport-specific rule blocks appended to the Pass 1 prompt."""
        return ()

    @property
    def verifier_rules(self) -> tuple[PromptRuleBlock, ...]:
        """Sport-specific rule blocks appended to the verifier prompt."""
        return ()

    @property
    def verifier_confusable_sets(self) -> tuple[frozenset[DefinitionKey], ...]:
        """Groups of event definitions the verifier may relabel between."""
        return ()

    @property
    def pass2_crop_subject(self) -> str:
        """Description of the cropped subject used by the Pass 2 cap-number prompt."""
        return "cropped player head/cap frames"

    @property
    def pass2_occlusion_hint(self) -> str:
        """Typical visual obstruction that hides a cap number in Pass 2 crops."""
        return "blur, occlusion, or a bad viewing angle"

    def is_goalkeeper(self, entry: LineupEntry) -> bool:
        """Return True when the lineup entry plays the goalkeeper position."""
        return False

    def is_goalkeeper_event(self, definition: EventDefinition) -> bool:
        """Return True when the event is normally performed by the goalkeeper."""
        return definition_key(definition) in self.goalkeeper_event_keys

    def is_conceded_event(self, definition: EventDefinition) -> bool:
        """Return True for events credited to the tracked team although the opponent acts."""
        return definition_key(definition) in self.conceded_event_keys

    def are_confusable(self, first: EventDefinition, second: EventDefinition) -> bool:
        """Return True when the verifier may relabel ``first`` as ``second`` (or vice versa)."""
        first_key, second_key = definition_key(first), definition_key(second)
        if first_key == second_key:
            return False
        return any(
            first_key in group and second_key in group for group in self.verifier_confusable_sets
        )

    def relabel_changes_actor(self, previous: EventDefinition, relabeled: EventDefinition) -> bool:
        """Return True when relabeling ``previous`` as ``relabeled`` implies a different actor.

        The acting player changes when the goalkeeper-event status differs (for example
        ``Steal`` <-> ``Save``) or when the conceded-event status differs, i.e. the acting team
        flips with the goal polarity (``Goal`` POSITIVE <-> ``Goal`` NEGATIVE). Same-actor
        relabels such as ``Shot on Goal`` <-> ``Missed Shot`` return False.
        """
        return self.is_goalkeeper_event(previous) != self.is_goalkeeper_event(
            relabeled
        ) or self.is_conceded_event(previous) != self.is_conceded_event(relabeled)

    def expected_actor_cap_color(
        self, definition: EventDefinition, tracked_hint: str | None
    ) -> str | None:
        """Cap color the acting player of ``definition`` is expected to wear.

        Goalkeeper-only events use the sport's goalkeeper cap color; every other event falls
        back to the tracked team's cap color hint.
        """
        if self.goalkeeper_cap_color and self.is_goalkeeper_event(definition):
            return self.goalkeeper_cap_color
        return tracked_hint

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

    _CONCEDED_EVENT_KEYS: frozenset[DefinitionKey] = frozenset({("goal", False)})

    @property
    def key(self) -> str:
        return "default"

    @property
    def conceded_event_keys(self) -> frozenset[DefinitionKey]:
        """Negative ``Goal`` events are conceded by the tracked team in every sport."""
        return self._CONCEDED_EVENT_KEYS

    @property
    def pass1_text(self) -> Pass1PromptText:
        return self._TEXT


class WaterPoloProfile(SportProfile):
    """Water polo conventions preserved verbatim from the pre-profile baseline."""

    _GOALKEEPER_POSITION_NAME = "goalkeeper"
    _GOALKEEPER_POSITION_SHORT = "gk"
    _GOALKEEPER_CAP_COLOR = "red"
    _GOALKEEPER_EVENT_KEYS: frozenset[DefinitionKey] = frozenset(
        {("goal", False), ("save", True), ("goalkeeper error", False)}
    )
    _CONCEDED_EVENT_KEYS: frozenset[DefinitionKey] = frozenset({("goal", False)})
    _CONFUSABLE_SETS: tuple[frozenset[DefinitionKey], ...] = (
        frozenset({("shot on goal", True), ("missed shot", False)}),
        frozenset({("steal", True), ("save", True)}),
        frozenset({("goal", True), ("goal", False)}),
    )
    _PASS1_RULES: tuple[PromptRuleBlock, ...] = (
        PromptRuleBlock(
            title="Pool Orientation And Attack Direction",
            rules=(
                "At the start of the chunk, locate the two goals and decide whether the TRACKED "
                "TEAM attacks the LEFT or the RIGHT goal (use the cap colors and the goalkeepers). "
                "Report it as `tracked_attack_direction` (`left`, `right` or `unknown`).",
                "Bind that direction to every event: the tracked team attacks toward its attack "
                "goal and defends the opposite goal. Teams can change ends between periods, so "
                "detect the direction again in every clip.",
                "Set `acting_team` on every event to `tracked`, `opponent` or `unknown`, based on "
                "the cap color of the acting player.",
            ),
        ),
        PromptRuleBlock(
            title="Goalkeeper Red Cap Rule",
            rules=(
                "Goalkeepers of BOTH teams wear RED caps. Never use the red cap to decide which "
                "team a player belongs to.",
                "A red cap defending near the goal the tracked team defends is the tracked "
                "team's goalkeeper.",
                "A red cap near the goal the tracked team attacks is the OPPONENT goalkeeper. "
                "When that goalkeeper stops a shot, the tracked team's event is a "
                "`Shot on Goal` and NOT a `Save`; never report a `Save` for the opponent "
                "goalkeeper.",
            ),
        ),
        PromptRuleBlock(
            title="Event Disambiguation",
            rules=(
                "`Shot on Goal` vs `Missed Shot`: a shot stopped by the goalkeeper or hitting the "
                "goal frame is a `Shot on Goal`; a shot that clearly misses the goal frame or is "
                "blocked by a field player is a `Missed Shot`.",
                "`Steal` vs `Save`: a `Steal` takes the ball from a field player; a `Save` is a "
                "stop by the tracked team's own goalkeeper of an opponent shot.",
                "`Goal` POSITIVE vs `Goal` NEGATIVE: decide the polarity from the goal the ball "
                "ENTERED, never from the referee signal alone. A ball that enters "
                "the goal defended by the tracked team (the goal of the tracked team's red-cap "
                "goalkeeper) is ALWAYS the NEGATIVE `Goal`, even when the pool zone is the "
                "tracked team's own goal. A ball that enters the opponent's goal is ALWAYS the "
                "POSITIVE `Goal`.",
                "After a goal the team that CONCEDED it restarts play from the center. If the "
                "tracked team restarts from the center, the tracked team conceded: report the "
                "NEGATIVE "
                "`Goal`. If the opponent restarts from the center, the tracked team scored: "
                "report the POSITIVE `Goal`.",
            ),
        ),
        PromptRuleBlock(
            title="Dead Time",
            rules=(
                "Do NOT report gameplay events during dead time: after a goal until the restart, "
                "during timeouts, injuries, review pauses, and between periods.",
                "A `StoppageStart` anchor marks the whistle that stops the play; report events "
                "that happened before that whistle, never after it.",
            ),
        ),
        PromptRuleBlock(
            title="Conceded Goals",
            rules=(
                "A conceded goal (`Goal` NEGATIVE) is scored by the opponent into the goal "
                "defended by the tracked team. The acting player for a conceded goal is the "
                "tracked team's goalkeeper (red cap, near the tracked team's own goal). "
                "Provide `actor_bbox_normalized` around that goalkeeper whenever visible.",
            ),
        ),
    )
    _VERIFIER_RULES: tuple[PromptRuleBlock, ...] = (
        PromptRuleBlock(
            title="Goal Polarity Check",
            rules=(
                "Goalkeepers of BOTH teams wear RED caps; use the position of the red-cap "
                "goalkeeper to find which goal the tracked team defends. Use the tracked-team "
                "attack direction given for each clip: the tracked team attacks that goal and "
                "defends the opposite one.",
                "A goal scored into the tracked team's DEFENDING goal (where its red-cap "
                "goalkeeper stands) MUST be the NEGATIVE `Goal` (conceded goal), NEVER the "
                "POSITIVE `Goal`. If a clip is labeled with the POSITIVE `Goal` but the ball "
                "entered the defended goal, answer `relabel` with the NEGATIVE `Goal`.",
                "A goal scored into the OPPONENT's goal MUST be the POSITIVE `Goal`. If it is "
                "labeled NEGATIVE, answer `relabel` with the POSITIVE `Goal`.",
                "After a goal the team that CONCEDED it restarts play from the center: the "
                "tracked team restarting from the center means a conceded (NEGATIVE) `Goal`; "
                "the opponent restarting from the center means the tracked team scored "
                "(POSITIVE) `Goal`.",
                "For a conceded goal the scorer belongs to the opponent (`acting_team` "
                "`opponent`) and the actor cap color is the tracked goalkeeper's red.",
            ),
        ),
    )
    _TEXT = Pass1PromptText(
        conceded_events_clause=(
            "(plus negative events conceded by the tracked team, such as a conceded goal) "
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

    @property
    def goalkeeper_cap_color(self) -> str | None:
        return self._GOALKEEPER_CAP_COLOR

    @property
    def goalkeeper_event_keys(self) -> frozenset[DefinitionKey]:
        return self._GOALKEEPER_EVENT_KEYS

    @property
    def conceded_event_keys(self) -> frozenset[DefinitionKey]:
        return self._CONCEDED_EVENT_KEYS

    @property
    def pass1_rules(self) -> tuple[PromptRuleBlock, ...]:
        return self._PASS1_RULES

    @property
    def verifier_rules(self) -> tuple[PromptRuleBlock, ...]:
        return self._VERIFIER_RULES

    @property
    def verifier_confusable_sets(self) -> tuple[frozenset[DefinitionKey], ...]:
        return self._CONFUSABLE_SETS

    @property
    def pass2_crop_subject(self) -> str:
        return "cropped Water Polo player head/cap frames"

    @property
    def pass2_occlusion_hint(self) -> str:
        return "water splash, blur, or angle"

    def is_goalkeeper(self, entry: LineupEntry) -> bool:
        name = entry.position_name.strip().casefold()
        short = entry.position_short_name.strip().casefold()
        return name == self._GOALKEEPER_POSITION_NAME or short == self._GOALKEEPER_POSITION_SHORT


_WATER_POLO_IDENTIFIERS = frozenset({"wp", "waterpolo", "water polo", "water-polo"})


def resolve_sport_profile(sport: SportSnapshot) -> SportProfile:
    """Resolve the profile for a snapshot sport, falling back to :class:`DefaultProfile`."""
    identifiers = {sport.short_name.strip().casefold(), sport.name.strip().casefold()}
    if identifiers & _WATER_POLO_IDENTIFIERS:
        return WaterPoloProfile()
    return DefaultProfile()
