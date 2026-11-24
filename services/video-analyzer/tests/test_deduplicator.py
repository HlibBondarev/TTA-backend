"""Tests for Pass 1 event deduplication and promptCode-to-UUID resolution."""

from __future__ import annotations

from pathlib import Path
from uuid import UUID

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.deduplicator import EventDeduplicator
from video_analyzer.pass1.models import Pass1ChunkResult, Pass1RawEvent
from video_analyzer.pass2.cap_resolver import CapResolutionResult
from video_analyzer.pass2.lineup_matcher import LineupMatcher
from video_analyzer.schemas.request import MatchSnapshot


def test_deduplicator_preserves_positive_vs_negative_goal_uuids(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk_result = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=25.0,
                prompt_code="EVT_01_G_POS",
                period_number=1,
                is_lead_to_goal=True,
                actor_bbox_normalized=[0.4, 0.4, 0.5, 0.6],
                confidence=0.92,
            ),
            Pass1RawEvent(
                time_sec=50.0,
                prompt_code="EVT_14_GC_NEG",
                period_number=1,
                is_lead_to_goal=True,
                confidence=0.88,
            ),
        ]
    )

    events, raw_count, warnings = EventDeduplicator().process(
        chunk_results=[(chunk, chunk_result)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=120.0,
        min_confidence=0.65,
    )

    assert raw_count == 2
    assert warnings == []
    assert len(events) == 2
    # Both share name == "Goal", but resolve to distinct seeded UUIDs
    assert events[0].definition.name == "Goal"
    assert events[1].definition.name == "Goal"
    assert events[0].definition.event_definition_id == UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f57")
    assert events[0].definition.is_positive is True
    assert events[1].definition.event_definition_id == UUID("6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f72")
    assert events[1].definition.is_positive is False


def test_deduplicator_merges_overlapping_chunks_and_warns_on_unknown_code(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    res0 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=112.0,  # abs = 112.0s
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.78,
            ),
            Pass1RawEvent(
                time_sec=40.0,
                prompt_code="INVENTED_CODE",
                period_number=1,
                confidence=0.95,
            ),
            Pass1RawEvent(
                time_sec=60.0,
                prompt_code="EVT_03_SP_POS",
                period_number=1,
                confidence=0.40,  # below min_confidence
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=7.8,  # abs = 112.8s (within 2.5s overlap window)
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                is_lead_to_goal=True,
                actor_bbox_normalized=[0.2, 0.3, 0.3, 0.5],
                confidence=0.91,
            )
        ]
    )

    events, raw_count, warnings = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[(chunk0, res0), (chunk1, res1)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )

    assert raw_count == 4
    assert len(warnings) == 1
    assert "INVENTED_CODE" in warnings[0]
    assert len(events) == 1
    assert events[0].confidence == 0.91
    assert events[0].video_time_sec == 112.8
    assert events[0].is_lead_to_goal is True
    assert events[0].actor_bbox_normalized == [0.2, 0.3, 0.3, 0.5]


def test_deduplicator_never_merges_same_chunk_events_and_preserves_bbox_timestamps(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    # Two distinct shots in the same chunk within 2.5s must stay separate
    res0 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=50.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.80,
            ),
            Pass1RawEvent(
                time_sec=51.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.85,
            ),
            # Overlap candidate at 112.0s WITH bbox, lower confidence than chunk 1's 112.8s
            Pass1RawEvent(
                time_sec=112.0,
                prompt_code="EVT_03_SP_POS",
                period_number=1,
                actor_bbox_normalized=[0.1, 0.1, 0.2, 0.2],
                confidence=0.75,
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        period_events=[
            # Overlap candidate at 112.8s WITHOUT bbox, higher confidence
            Pass1RawEvent(
                time_sec=7.8,
                prompt_code="EVT_03_SP_POS",
                period_number=1,
                actor_bbox_normalized=None,
                confidence=0.92,
            )
        ]
    )

    events, _, warnings = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[(chunk0, res0), (chunk1, res1)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )

    assert warnings == []
    same_chunk_shots = [e for e in events if e.definition.prompt_code == "EVT_02_ST_POS"]
    assert len(same_chunk_shots) == 2

    merged_sprint = [e for e in events if e.definition.prompt_code == "EVT_03_SP_POS"]
    assert len(merged_sprint) == 1
    assert merged_sprint[0].video_time_sec == 112.8
    # Must NOT attach 112.0s bbox onto the 112.8s retained event
    assert merged_sprint[0].actor_bbox_normalized is None


def test_deduplicator_maintains_sorted_order_on_replacement_and_rejects_oob_offsets(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")
    chunk2 = VideoChunk(index=2, start_sec=110.0, end_sec=230.0, path=tmp_path / "c2.mp4")

    res0 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=110.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.70,
            ),
            Pass1RawEvent(
                time_sec=111.0,
                prompt_code="EVT_03_SP_POS",
                period_number=1,
                confidence=0.90,
            ),
            Pass1RawEvent(
                time_sec=145.0,  # exceeds chunk0 duration (120.0s)
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.95,
            ),
        ]
    )
    res1 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=7.4,  # abs = 112.4s -> replaces 110.0s (crosses 111.0s)
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.95,
            )
        ]
    )
    res2 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=4.5,  # abs = 114.5s -> still within 2.5s of 112.4s
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                confidence=0.85,
            )
        ]
    )

    events, _, warnings = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[(chunk0, res0), (chunk1, res1), (chunk2, res2)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )

    assert len(warnings) == 1
    assert "out-of-range" in warnings[0]
    assert len([e for e in events if e.definition.prompt_code == "EVT_02_ST_POS"]) == 1
    assert len([e for e in events if e.definition.prompt_code == "EVT_03_SP_POS"]) == 1


def test_deduplicator_does_not_merge_incompatible_cross_chunk_shots(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    # 1. Spatially disjoint shots at 112s and 114s across overlapping chunks
    events_bbox, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            actor_bbox_normalized=[0.10, 0.35, 0.20, 0.60],
                            confidence=0.88,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,  # abs = 114.0s
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            actor_bbox_normalized=[0.75, 0.35, 0.85, 0.60],
                            confidence=0.90,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_bbox) == 2

    # 2. Conflicting pool zones at 112s and 114s across overlapping chunks
    events_zone, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            pool_zone_hint="left_wing_5m",
                            confidence=0.88,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            pool_zone_hint="right_wing_5m",
                            confidence=0.90,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_zone) == 2

    # 3. Conflicting cap colors at 112s and 114s across overlapping chunks
    events_cap, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            cap_color_hint="white",
                            confidence=0.88,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            cap_color_hint="blue",
                            confidence=0.90,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_cap) == 2

    # 4. Different period numbers at 112s and 114s across overlapping chunks
    events_period, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            confidence=0.88,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=2,
                            confidence=0.90,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_period) == 2

    # 5. Non-overlapping adjacent chunks reporting near boundary stay separate
    adj_chunk1 = VideoChunk(index=1, start_sec=120.0, end_sec=240.0, path=tmp_path / "c1_adj.mp4")
    events_non_overlap, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=119.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            confidence=0.88,
                        )
                    ]
                ),
            ),
            (
                adj_chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=1.0,  # abs = 121.0s
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            confidence=0.90,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_non_overlap) == 2

    # 6. Compatible cross-chunk duplicate in overlap zone still merges
    events_compatible, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            cap_color_hint="white",
                            pool_zone_hint="center_5m",
                            actor_bbox_normalized=[0.42, 0.35, 0.52, 0.60],
                            confidence=0.82,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            cap_color_hint="White",
                            pool_zone_hint="center 5m",
                            actor_bbox_normalized=[0.44, 0.36, 0.54, 0.61],
                            confidence=0.91,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_compatible) == 1
    assert events_compatible[0].confidence == 0.91
    assert events_compatible[0].needs_review is True
    assert "AMBIGUOUS_OVERLAP_MERGE" in events_compatible[0].review_reasons


def test_deduplicator_flags_ambiguous_missing_hint_cross_chunk_merge_for_review(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    # Case A: Both overlapping shots at 112s and 114s lack optional hints
    events_no_hints, _, warnings = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            confidence=0.86,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,  # abs = 114.0s
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            confidence=0.92,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert warnings == []
    assert len(events_no_hints) == 1
    assert events_no_hints[0].needs_review is True
    assert "AMBIGUOUS_OVERLAP_MERGE" in events_no_hints[0].review_reasons

    # Case B: One shot has hints and the other lacks all hints -> still flagged ambiguous
    events_one_sided, _, _ = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            confidence=0.84,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            actor_bbox_normalized=[0.4, 0.3, 0.5, 0.6],
                            cap_color_hint="white",
                            pool_zone_hint="center_5m",
                            confidence=0.92,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )
    assert len(events_one_sided) == 1
    assert events_one_sided[0].needs_review is True
    assert "AMBIGUOUS_OVERLAP_MERGE" in events_one_sided[0].review_reasons

    outcome = LineupMatcher().resolve(
        events_one_sided[0],
        CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Sharp cap #7",
        ),
        sample_snapshot,
        min_cap_confidence=0.80,
    )
    assert outcome.needs_review is True
    assert "AMBIGUOUS_OVERLAP_MERGE" in outcome.review_reasons


def test_deduplicator_flags_cross_chunk_shots_with_matching_hints_for_review(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    chunk1 = VideoChunk(index=1, start_sec=105.0, end_sec=225.0, path=tmp_path / "c1.mp4")

    # Two distinct rapid shots (e.g. shot + rebound) at 112s and 114s sharing identical hints
    events, _, warnings = EventDeduplicator(merge_window_sec=2.5).process(
        chunk_results=[
            (
                chunk0,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=112.0,
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            cap_color_hint="white",
                            pool_zone_hint="center_5m",
                            actor_bbox_normalized=[0.42, 0.35, 0.52, 0.60],
                            confidence=0.88,
                        )
                    ]
                ),
            ),
            (
                chunk1,
                Pass1ChunkResult(
                    period_events=[
                        Pass1RawEvent(
                            time_sec=9.0,  # abs = 114.0s
                            prompt_code="EVT_02_ST_POS",
                            period_number=1,
                            cap_color_hint="white",
                            pool_zone_hint="center_5m",
                            actor_bbox_normalized=[0.43, 0.36, 0.53, 0.61],
                            confidence=0.93,
                        )
                    ]
                ),
            ),
        ],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=240.0,
        min_confidence=0.65,
    )

    assert warnings == []
    assert len(events) == 1
    assert events[0].needs_review is True
    assert "AMBIGUOUS_OVERLAP_MERGE" in events[0].review_reasons

    outcome = LineupMatcher().resolve(
        events[0],
        CapResolutionResult(
            resolved_cap_number=7,
            confidence=0.95,
            candidate_scores={7: 0.95},
            reasoning="Sharp cap #7",
        ),
        sample_snapshot,
        min_cap_confidence=0.80,
    )
    assert outcome.needs_review is True
    assert "AMBIGUOUS_OVERLAP_MERGE" in outcome.review_reasons


def test_deduplicator_rejects_bboxes_collapsing_to_zero_after_rounding(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk0 = VideoChunk(index=0, start_sec=0.0, end_sec=120.0, path=tmp_path / "c0.mp4")
    res0 = Pass1ChunkResult(
        period_events=[
            Pass1RawEvent(
                time_sec=10.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                actor_bbox_normalized=[0.50000, 0.20000, 0.50004, 0.60000],
                confidence=0.90,
            ),
            Pass1RawEvent(
                time_sec=20.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                actor_bbox_normalized=[0.20000, 0.50000, 0.60000, 0.50004],
                confidence=0.90,
            ),
            Pass1RawEvent(
                time_sec=30.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                actor_bbox_normalized=[0.50000, 0.20000, 0.50010, 0.60000],
                confidence=0.90,
            ),
            Pass1RawEvent(
                time_sec=40.0,
                prompt_code="EVT_02_ST_POS",
                period_number=1,
                actor_bbox_normalized=[0.1, 0.2, float("nan"), 0.5],
                confidence=0.90,
            ),
        ]
    )

    events, _, warnings = EventDeduplicator().process(
        chunk_results=[(chunk0, res0)],
        event_definitions=sample_snapshot.event_definitions,
        total_duration_sec=120.0,
        min_confidence=0.65,
    )

    assert warnings == []
    assert len(events) == 4
    assert events[0].actor_bbox_normalized is None
    assert events[1].actor_bbox_normalized is None
    assert events[2].actor_bbox_normalized == [0.5, 0.2, 0.5001, 0.6]
    assert events[3].actor_bbox_normalized is None
