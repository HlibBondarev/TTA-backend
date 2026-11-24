"""Tests for Pass 1 Gemini client response validation."""

from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.pass2.cap_resolver import GeminiCropGridCapResolver
from video_analyzer.pass2.crop_selector import CropCandidate, SelectedCrops
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


def _contains_key(obj: object, target_key: str) -> bool:
    if isinstance(obj, dict):
        if target_key in obj:
            return True
        return any(_contains_key(v, target_key) for v in obj.values())
    if isinstance(obj, list):
        return any(_contains_key(item, target_key) for item in obj)
    return False


def test_gemini_clients_configure_http_timeout_and_default_free_schemas(
    monkeypatch: pytest.MonkeyPatch,
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    captured_client_kwargs: list[dict[str, object]] = []
    captured_configs: list[object] = []

    class RecordingClient:
        def __init__(self, **kwargs) -> None:
            captured_client_kwargs.append(kwargs)
            self.files = SimpleNamespace(
                upload=lambda file: SimpleNamespace(
                    name="files/chunk-0",
                    state=SimpleNamespace(name="ACTIVE"),
                ),
                delete=lambda name: None,
            )
            self.models = SimpleNamespace(generate_content=self._generate_content)

        @staticmethod
        def _generate_content(**kwargs):
            captured_configs.append(kwargs["config"])
            return SimpleNamespace(
                parsed=None,
                text='{"period_events": [], "time_anchors": [], '
                '"tile_readings": [], "candidate_numbers": [], '
                '"confidence": 0.0, "reasoning": ""}',
            )

    monkeypatch.setattr("google.genai.Client", RecordingClient)

    pass1 = GeminiPass1Analyzer(api_key="test-key", timeout_sec=45.0)
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=30.0, path=tmp_path / "c0.mp4")
    pass1.analyze_chunk(sample_snapshot, chunk)

    pass2 = GeminiCropGridCapResolver(api_key="test-key", timeout_sec=60.0)
    crop = np.zeros((32, 32, 3), dtype=np.uint8)
    selected = SelectedCrops(
        crops=[CropCandidate(0, 5.0, crop, 100.0)],
        contact_sheet_jpeg=b"jpeg",
        contact_sheet_bgr=crop,
    )
    pass2.resolve(selected, allowed_cap_numbers=[7, 9])

    assert len(captured_client_kwargs) == 2
    assert captured_client_kwargs[0]["http_options"].timeout == 45000  # type: ignore[union-attr]
    assert captured_client_kwargs[1]["http_options"].timeout == 60000  # type: ignore[union-attr]

    assert len(captured_configs) == 2
    for cfg in captured_configs:
        schema_dump = cfg.response_schema.model_dump(exclude_none=True)  # type: ignore[union-attr]
        assert not _contains_key(schema_dump, "default")
