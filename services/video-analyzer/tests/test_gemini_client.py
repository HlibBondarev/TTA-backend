"""Tests for Pass 1 Gemini client response validation."""

from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import pytest

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.schemas.request import MatchSnapshot


def _build_analyzer_with_response(response: object) -> GeminiPass1Analyzer:
    class FakeFiles:
        @staticmethod
        def upload(file: str):
            return SimpleNamespace(name="files/chunk-0", state=SimpleNamespace(name="ACTIVE"))

        @staticmethod
        def delete(name: str):
            return None

    class FakeModels:
        @staticmethod
        def generate_content(**kwargs):
            return response

    fake_client = SimpleNamespace(files=FakeFiles(), models=FakeModels())
    return GeminiPass1Analyzer(api_key="test-key", client=fake_client)


def test_gemini_pass1_raises_on_empty_response_without_parsed_result(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c0.mp4")

    analyzer_none = _build_analyzer_with_response(SimpleNamespace(parsed=None, text=None))
    with pytest.raises(RuntimeError, match="empty response"):
        analyzer_none.analyze_chunk(sample_snapshot, chunk)

    analyzer_blank = _build_analyzer_with_response(SimpleNamespace(parsed=None, text="   \n "))
    with pytest.raises(RuntimeError, match="empty response"):
        analyzer_blank.analyze_chunk(sample_snapshot, chunk)


def test_gemini_pass1_accepts_explicit_valid_empty_lists(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c0.mp4")

    analyzer_parsed = _build_analyzer_with_response(
        SimpleNamespace(parsed=Pass1ChunkResult(period_events=[], time_anchors=[]), text=None)
    )
    res_parsed = analyzer_parsed.analyze_chunk(sample_snapshot, chunk)
    assert res_parsed.period_events == []
    assert res_parsed.time_anchors == []

    analyzer_json = _build_analyzer_with_response(
        SimpleNamespace(parsed=None, text='{"period_events": [], "time_anchors": []}')
    )
    res_json = analyzer_json.analyze_chunk(sample_snapshot, chunk)
    assert res_json.period_events == []
    assert res_json.time_anchors == []
