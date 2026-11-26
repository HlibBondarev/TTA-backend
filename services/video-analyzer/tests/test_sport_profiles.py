"""SportProfile resolution, goalkeeper logic, and deterministic internal promptCode generation."""

from __future__ import annotations

import random
from uuid import uuid4

from tests.support import load_sample_snapshot, make_context
from video_analyzer.pass1.models import Pass1EventCandidate
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.schemas.request import EventDefinitionSnapshot, LineupEntry, SportSnapshot
from video_analyzer.sport import (
    DefaultProfile,
    WaterPoloProfile,
    build_event_catalog,
    resolve_sport_profile,
)


def _sport(name: str, short_name: str) -> SportSnapshot:
    return SportSnapshot(id=uuid4(), name=name, short_name=short_name)


def _entry(position_name: str, short: str, number: int = 1) -> LineupEntry:
    return LineupEntry(
        id=uuid4(),
        player_roster_id=uuid4(),
        first_name="A",
        last_name="B",
        number=number,
        position_id=uuid4(),
        position_name=position_name,
        position_short_name=short,
    )


def _definition(name: str, short: str, positive: bool = True) -> EventDefinitionSnapshot:
    return EventDefinitionSnapshot(id=uuid4(), name=name, short_name=short, is_positive=positive)


def test_resolves_water_polo_profile_from_sport_metadata() -> None:
    assert isinstance(resolve_sport_profile(_sport("Waterpolo", "WP")), WaterPoloProfile)
    assert isinstance(resolve_sport_profile(_sport("Water Polo", "x")), WaterPoloProfile)
    assert isinstance(resolve_sport_profile(_sport("Other", " wp ")), WaterPoloProfile)
    assert resolve_sport_profile(_sport("Waterpolo", "WP")).key == "water_polo"


def test_unknown_sport_falls_back_to_neutral_default_profile() -> None:
    profile = resolve_sport_profile(_sport("Football", "FB"))
    assert isinstance(profile, DefaultProfile)
    assert profile.key == "default"
    assert profile.goalkeeper_label_token is None
    assert profile.is_goalkeeper(_entry("Goalkeeper", "GK")) is False
    assert "water" not in profile.pass1_text.active_players_label.lower()
    assert "swim" not in profile.pass1_text.period_start_cue.lower()


def test_water_polo_profile_derives_goalkeeper_from_position_fields() -> None:
    profile = WaterPoloProfile()
    assert profile.is_goalkeeper(_entry("Goalkeeper", "GK"))
    assert profile.is_goalkeeper(_entry(" goalkeeper ", "x"))
    assert profile.is_goalkeeper(_entry("Custom", "gk"))
    assert not profile.is_goalkeeper(_entry("Driver", "D"))
    assert profile.goalkeeper_label_token == "goalkeeper"


def test_water_polo_profile_identifies_goalkeeper_events() -> None:
    catalog = make_context().event_catalog
    by_name = {d.name: d for d in catalog if d.is_positive or d.name != "Goal"}
    profile = WaterPoloProfile()
    assert profile.is_goalkeeper_event(by_name["Save"])
    assert profile.is_goalkeeper_event(by_name["Goalkeeper Error"])
    assert not profile.is_goalkeeper_event(by_name["Steal"])
    assert not DefaultProfile().is_goalkeeper_event(by_name["Save"])


def test_goalkeeper_fallback_ranking_depends_on_the_profile() -> None:
    snapshot = load_sample_snapshot()
    save = next(d for d in make_context(snapshot).event_catalog if d.name == "Save")
    candidate = Pass1EventCandidate(
        video_time_sec=1.0,
        definition=save,
        period_number=1,
        is_lead_to_goal=False,
        actor_bbox_normalized=None,
        pool_zone_hint=None,
        cap_color_hint=None,
        confidence=0.9,
    )

    water_polo = LineupMatcher().resolve(
        candidate, None, make_context(snapshot, profile=WaterPoloProfile()), 0.8
    )
    neutral = LineupMatcher().resolve(
        candidate, None, make_context(snapshot, profile=DefaultProfile()), 0.8
    )

    assert water_polo.candidate_lineup_ids[0].cap_number in (1, 13)
    assert water_polo.candidate_lineup_ids[0].reason == "Goalkeeper role heuristic"
    assert {c.reason for c in neutral.candidate_lineup_ids} == {"Team lineup candidate"}


def test_prompt_codes_are_unique_deterministic_and_bijective_with_ids() -> None:
    definitions = load_sample_snapshot().event_definitions
    first = build_event_catalog(definitions)
    second = build_event_catalog(definitions)

    assert [d.prompt_code for d in first] == [d.prompt_code for d in second]
    codes = [d.prompt_code for d in first]
    assert len(codes) == len(set(codes)) == 34
    assert {d.event_definition_id for d in first} == {d.id for d in definitions}
    assert [d.event_definition_id for d in first] == [d.id for d in definitions]
    assert all(c.startswith("EVT_") and c.endswith(("_POS", "_NEG")) for c in codes)


def test_prompt_codes_do_not_depend_on_input_order() -> None:
    definitions = list(load_sample_snapshot().event_definitions)
    shuffled = definitions[:]
    random.Random(7).shuffle(shuffled)

    expected = {d.event_definition_id: d.prompt_code for d in build_event_catalog(definitions)}
    actual = {d.event_definition_id: d.prompt_code for d in build_event_catalog(shuffled)}
    assert actual == expected


def test_duplicate_names_get_distinct_codes_with_polarity_suffix() -> None:
    catalog = build_event_catalog([_definition("Goal", "G", True), _definition("Goal", "G", False)])
    positive, negative = catalog
    assert positive.prompt_code.endswith("_G_POS")
    assert negative.prompt_code.endswith("_G_NEG")
    assert positive.prompt_code != negative.prompt_code


def test_prompt_code_sanitizes_short_names_with_a_fallback() -> None:
    catalog = build_event_catalog(
        [_definition("A", "red card (18s)"), _definition("B", "  ---  "), _definition("C", "")]
    )
    codes = [d.prompt_code for d in catalog]
    assert codes[0] == "EVT_01_REDCARD18S_POS"
    assert codes[1] == "EVT_02_X_POS"
    assert codes[2] == "EVT_03_X_POS"
