"""Tests for Pass 1 Gemini client response validation."""

from __future__ import annotations

from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest
from google.genai import errors as genai_errors
from google.genai import types as genai_types

from video_analyzer.config import Settings
from video_analyzer.gemini.resilience import (
    BreakerReason,
    GeminiCallBlockedError,
    GeminiJobScope,
    GeminiResilience,
    job_scope,
)
from video_analyzer.media.ffmpeg_client import VideoChunk
from video_analyzer.pass1.gemini_client import GeminiPass1Analyzer
from video_analyzer.pass1.models import Pass1ChunkResult
from video_analyzer.pass1.prompt_builder import Pass1PromptBuilder
from video_analyzer.pass2.cap_resolver import (
    CapResolutionError,
    CapResolverFactory,
    GeminiCropGridCapResolver,
)
from video_analyzer.pass2.crop_selector import CropCandidate, SelectedCrops
from video_analyzer.pipeline.factory import build_default_orchestrator
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
    monkeypatch: pytest.MonkeyPatch,
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    monkeypatch.setattr("video_analyzer.pass1.gemini_client.time.sleep", lambda _: None)
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


def test_gemini_pass1_requires_explicit_top_level_keys_in_dict_and_json(
    monkeypatch: pytest.MonkeyPatch,
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    monkeypatch.setattr("video_analyzer.pass1.gemini_client.time.sleep", lambda _: None)
    chunk = VideoChunk(index=2, start_sec=0.0, end_sec=60.0, path=tmp_path / "c2.mp4")

    for bad_response in (
        SimpleNamespace(parsed={"period_events": []}, text=None),
        SimpleNamespace(parsed={"time_anchors": []}, text=None),
        SimpleNamespace(parsed=None, text='{"period_events": []}'),
        SimpleNamespace(parsed=None, text='{"time_anchors": []}'),
    ):
        analyzer = _build_analyzer_with_response(bad_response)
        with pytest.raises(RuntimeError, match="missing required keys"):
            analyzer.analyze_chunk(sample_snapshot, chunk)

    dict_empty = _build_analyzer_with_response(
        SimpleNamespace(parsed={"period_events": [], "time_anchors": []}, text=None)
    )
    res_dict = dict_empty.analyze_chunk(sample_snapshot, chunk)
    assert res_dict.period_events == []
    assert res_dict.time_anchors == []


def test_gemini_pass1_retries_malformed_json_inside_retry_loop(
    monkeypatch: pytest.MonkeyPatch,
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    monkeypatch.setattr("video_analyzer.pass1.gemini_client.time.sleep", lambda _: None)
    chunk = VideoChunk(index=1, start_sec=0.0, end_sec=60.0, path=tmp_path / "c1.mp4")

    def build_sequenced_analyzer(responses: list[object]) -> tuple[GeminiPass1Analyzer, list[int]]:
        calls: list[int] = []

        class FakeFiles:
            @staticmethod
            def upload(file: str):
                return SimpleNamespace(name="files/chunk-1", state=SimpleNamespace(name="ACTIVE"))

            @staticmethod
            def delete(name: str):
                return None

        class FakeModels:
            @staticmethod
            def generate_content(**kwargs):
                idx = len(calls)
                calls.append(idx + 1)
                return responses[idx]

        fake_client = SimpleNamespace(files=FakeFiles(), models=FakeModels())
        return GeminiPass1Analyzer(api_key="test-key", client=fake_client), calls

    # Recovers on 3rd attempt when first two return malformed JSON
    recovering_analyzer, recovering_calls = build_sequenced_analyzer(
        [
            SimpleNamespace(parsed=None, text="{not json"),
            SimpleNamespace(parsed=None, text='{"period_events": [}'),
            SimpleNamespace(parsed=None, text='{"period_events": [], "time_anchors": []}'),
        ]
    )
    res = recovering_analyzer.analyze_chunk(sample_snapshot, chunk)
    assert res.period_events == []
    assert res.time_anchors == []
    assert recovering_calls == [1, 2, 3]

    # Exhausts all 3 retries and raises clear RuntimeError when all fail
    failing_analyzer, failing_calls = build_sequenced_analyzer(
        [
            SimpleNamespace(parsed=None, text="{bad-1"),
            SimpleNamespace(parsed=None, text="{bad-2"),
            SimpleNamespace(parsed=None, text="{bad-3"),
        ]
    )
    with pytest.raises(RuntimeError, match="invalid JSON for chunk 1"):
        failing_analyzer.analyze_chunk(sample_snapshot, chunk)
    assert failing_calls == [1, 2, 3]


def test_pass1_prompt_builder_uses_full_unrounded_chunk_duration(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    builder = Pass1PromptBuilder()

    chunk_precise = VideoChunk(index=0, start_sec=0.0, end_sec=10.126, path=tmp_path / "c0.mp4")
    prompt_precise = builder.build_chunk_prompt(sample_snapshot, chunk_precise)
    assert "(duration 10.126s)" in prompt_precise
    assert "(0.0 to 10.126)" in prompt_precise
    assert "(0.0 to 10.13)" not in prompt_precise

    chunk_sub = VideoChunk(index=1, start_sec=105.1, end_sec=120.3, path=tmp_path / "c1.mp4")
    prompt_sub = builder.build_chunk_prompt(sample_snapshot, chunk_sub)
    assert "(duration 15.2s)" in prompt_sub
    assert "(0.0 to 15.2)" in prompt_sub


def test_gemini_clients_extract_usage_metadata_from_responses(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c0.mp4")
    pass1_usage_meta = SimpleNamespace(
        prompt_token_count=4000,
        candidates_token_count=350,
        thoughts_token_count=150,
        cached_content_token_count=500,
        total_token_count=4500,
    )
    analyzer = _build_analyzer_with_response(
        SimpleNamespace(
            parsed=None,
            text='{"period_events": [], "time_anchors": []}',
            usage_metadata=pass1_usage_meta,
        )
    )
    pass1_res = analyzer.analyze_chunk(sample_snapshot, chunk)
    assert pass1_res.token_usage is not None
    assert pass1_res.token_usage.call_count == 1
    assert pass1_res.token_usage.prompt_tokens == 4000
    assert pass1_res.token_usage.candidates_tokens == 350
    assert pass1_res.token_usage.thoughts_tokens == 150
    assert pass1_res.token_usage.cached_content_tokens == 500
    assert pass1_res.token_usage.total_tokens == 4500

    pass2_usage_meta = SimpleNamespace(
        prompt_token_count=800,
        candidates_token_count=90,
        thoughts_token_count=30,
        cached_content_token_count=0,
        total_token_count=920,
    )
    fake_pass2_client = SimpleNamespace(
        models=SimpleNamespace(
            generate_content=lambda **kwargs: SimpleNamespace(
                parsed=None,
                text=(
                    '{"tile_readings": [{"tile_index": 1, "cap_number": 7, "confidence": 0.95}], '
                    '"consensus_cap_number": 7, "confidence": 0.95, '
                    '"candidate_numbers": [{"cap_number": 7, "score": 0.95}], '
                    '"reasoning": "Clear cap #7"}'
                ),
                usage_metadata=pass2_usage_meta,
            )
        )
    )
    resolver = GeminiCropGridCapResolver(api_key="test-key", client=fake_pass2_client)
    crop = np.zeros((32, 32, 3), dtype=np.uint8)
    selected = SelectedCrops(
        crops=[CropCandidate(0, 5.0, crop, 100.0)],
        contact_sheet_jpeg=b"jpeg",
        contact_sheet_bgr=crop,
    )
    cap_res = resolver.resolve(selected, allowed_cap_numbers=[7, 9])
    assert cap_res.resolved_cap_number == 7
    assert cap_res.token_usage is not None
    assert cap_res.token_usage.call_count == 1
    assert cap_res.token_usage.prompt_tokens == 800
    assert cap_res.token_usage.candidates_tokens == 90
    assert cap_res.token_usage.thoughts_tokens == 30
    assert cap_res.token_usage.total_tokens == 920


def test_gemini_pass1_accumulates_usage_across_retries_without_double_counting(
    monkeypatch: pytest.MonkeyPatch,
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    monkeypatch.setattr("video_analyzer.pass1.gemini_client.time.sleep", lambda _: None)
    chunk = VideoChunk(index=0, start_sec=0.0, end_sec=60.0, path=tmp_path / "c0.mp4")

    responses = [
        SimpleNamespace(
            parsed=None,
            text="{bad json",
            usage_metadata=SimpleNamespace(
                prompt_token_count=1000,
                candidates_token_count=50,
                thoughts_token_count=10,
                cached_content_token_count=None,
                total_token_count=1060,
            ),
        ),
        SimpleNamespace(
            parsed=None,
            text='{"period_events": [], "time_anchors": []}',
            usage_metadata=SimpleNamespace(
                prompt_token_count=1200,
                candidates_token_count=80,
                thoughts_token_count=20,
                cached_content_token_count=None,
                total_token_count=1300,
            ),
        ),
    ]
    call_idx = 0

    class FakeClient:
        files = SimpleNamespace(
            upload=lambda file: SimpleNamespace(
                name="files/chunk-0",
                state=SimpleNamespace(name="ACTIVE"),
            ),
            delete=lambda name: None,
        )

        @staticmethod
        def _generate_content(**kwargs):
            nonlocal call_idx
            resp = responses[call_idx]
            call_idx += 1
            return resp

        models = SimpleNamespace(generate_content=_generate_content)

    analyzer = GeminiPass1Analyzer(api_key="test-key", client=FakeClient())
    res = analyzer.analyze_chunk(sample_snapshot, chunk)
    assert res.token_usage is not None
    assert res.token_usage.call_count == 2
    assert res.token_usage.prompt_tokens == 2200
    assert res.token_usage.candidates_tokens == 130
    assert res.token_usage.thoughts_tokens == 30
    assert res.token_usage.total_tokens == 2360


def test_gemini_pass2_cap_resolver_attaches_usage_to_parse_failure_error() -> None:
    fake_client = SimpleNamespace(
        models=SimpleNamespace(
            generate_content=lambda **kwargs: SimpleNamespace(
                parsed=None,
                text="{invalid json",
                usage_metadata=SimpleNamespace(
                    prompt_token_count=600,
                    candidates_token_count=40,
                    thoughts_token_count=None,
                    cached_content_token_count=None,
                    total_token_count=640,
                ),
            )
        )
    )
    resolver = GeminiCropGridCapResolver(api_key="test-key", client=fake_client)
    crop = np.zeros((32, 32, 3), dtype=np.uint8)
    selected = SelectedCrops(
        crops=[CropCandidate(0, 5.0, crop, 100.0)],
        contact_sheet_jpeg=b"jpeg",
        contact_sheet_bgr=crop,
    )
    with pytest.raises(CapResolutionError) as exc_info:
        resolver.resolve(selected, allowed_cap_numbers=[7, 9])

    assert exc_info.value.token_usage is not None
    assert exc_info.value.token_usage.call_count == 1
    assert exc_info.value.token_usage.prompt_tokens == 600
    assert exc_info.value.token_usage.candidates_tokens == 40
    assert exc_info.value.token_usage.thoughts_tokens is None
    assert exc_info.value.token_usage.total_tokens == 640


# ---------------------------------------------------------------------------------------------
# Issue #157: thinking control, AFC disabled, resilience wiring
# ---------------------------------------------------------------------------------------------

_EMPTY_PASS1_JSON = '{"period_events": [], "time_anchors": []}'
_CAP_JSON = (
    '{"tile_readings": [], "consensus_cap_number": 7, "confidence": 0.95, '
    '"candidate_numbers": [{"cap_number": 7, "score": 0.95}], "reasoning": "ok"}'
)


class _RecordingGeminiClient:
    """Fake google-genai client recording every generate_content call's config."""

    def __init__(self, text: str, errors: list[Exception] | None = None) -> None:
        self.configs: list[object] = []
        self.generate_calls = 0
        self.upload_calls = 0
        self._text = text
        self._errors = list(errors or [])
        self.files = SimpleNamespace(
            upload=self._upload,
            delete=lambda name: None,
        )
        self.models = SimpleNamespace(generate_content=self._generate_content)

    def _upload(self, file: str):
        self.upload_calls += 1
        return SimpleNamespace(name="files/c", state=SimpleNamespace(name="ACTIVE"))

    def _generate_content(self, **kwargs):
        self.generate_calls += 1
        self.configs.append(kwargs["config"])
        if self._errors:
            raise self._errors.pop(0)
        return SimpleNamespace(parsed=None, text=self._text)


def _selected_crops() -> SelectedCrops:
    crop = np.zeros((32, 32, 3), dtype=np.uint8)
    return SelectedCrops(
        crops=[CropCandidate(0, 5.0, crop, 100.0)],
        contact_sheet_jpeg=b"jpeg",
        contact_sheet_bgr=crop,
    )


def _api_error(code: int, status: str, quota_id: str = "") -> Exception:
    details = [{"violations": [{"quotaId": quota_id}]}] if quota_id else []
    body = {"error": {"code": code, "message": status, "status": status, "details": details}}
    cls = genai_errors.ClientError if code < 500 else genai_errors.ServerError
    return cls(code, body)


def _fast_resilience(slot: str = "pass1") -> GeminiResilience:
    return GeminiResilience(slot, "model", sleep=lambda _: None, rng=lambda: 0.0)


def _chunk(tmp_path: Path) -> VideoChunk:
    return VideoChunk(index=0, start_sec=0.0, end_sec=30.0, path=tmp_path / "c0.mp4")


def test_unset_thinking_level_omits_thinking_config_but_still_disables_afc(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    pass1_client = _RecordingGeminiClient(_EMPTY_PASS1_JSON)
    GeminiPass1Analyzer(api_key="k", client=pass1_client).analyze_chunk(
        sample_snapshot, _chunk(tmp_path)
    )

    pass2_client = _RecordingGeminiClient(_CAP_JSON)
    GeminiCropGridCapResolver(api_key="k", client=pass2_client).resolve(
        _selected_crops(), allowed_cap_numbers=[7]
    )

    configs = [*pass1_client.configs, *pass2_client.configs]
    assert len(configs) == 2
    for config in configs:
        assert config.thinking_config is None
        assert config.automatic_function_calling.disable is True


def test_configured_thinking_level_sends_only_thinking_level(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    pass1_client = _RecordingGeminiClient(_EMPTY_PASS1_JSON)
    GeminiPass1Analyzer(api_key="k", client=pass1_client, thinking_level="LOW").analyze_chunk(
        sample_snapshot, _chunk(tmp_path)
    )
    pass2_client = _RecordingGeminiClient(_CAP_JSON)
    GeminiCropGridCapResolver(api_key="k", client=pass2_client, thinking_level="minimal").resolve(
        _selected_crops(), allowed_cap_numbers=[7]
    )

    pass1_cfg, pass2_cfg = pass1_client.configs[0], pass2_client.configs[0]
    assert pass1_cfg.thinking_config.thinking_level == genai_types.ThinkingLevel.LOW
    assert pass2_cfg.thinking_config.thinking_level == genai_types.ThinkingLevel.MINIMAL
    for config in (pass1_cfg, pass2_cfg):
        assert config.thinking_config.thinking_budget is None
        assert config.thinking_config.include_thoughts is None
        assert config.automatic_function_calling.disable is True


def test_factories_wire_per_slot_models_thinking_levels_and_resilience() -> None:
    settings = Settings(
        _env_file=None,
        gemini_pass1_thinking_level="MEDIUM",
        gemini_pass2_thinking_level=None,
        gemini_pass1_rpm=30,
        gemini_pass2_rpm=90,
        roboflow_api_key="rf",
        gemini_api_key="g",
    )
    resolver = CapResolverFactory.create(settings)
    assert isinstance(resolver, GeminiCropGridCapResolver)
    assert resolver._model_name == "gemini-3.5-flash-lite"
    assert resolver._thinking_level is None
    assert resolver._resilience.slot == "pass2"

    orchestrator = build_default_orchestrator(settings)
    pass1 = orchestrator._pass1
    assert isinstance(pass1, GeminiPass1Analyzer)
    assert pass1._model_name == "gemini-4-hydrogen"
    assert pass1._thinking_level == "MEDIUM"
    assert pass1._resilience.slot == "pass1"
    assert pass1._resilience._pacer._interval_sec == pytest.approx(2.0)


def test_pass1_transient_errors_are_retried_by_resilience_and_succeed(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    client = _RecordingGeminiClient(
        _EMPTY_PASS1_JSON, errors=[_api_error(503, "UNAVAILABLE"), _api_error(504, "TIMEOUT")]
    )
    analyzer = GeminiPass1Analyzer(api_key="k", client=client, resilience=_fast_resilience())

    result = analyzer.analyze_chunk(sample_snapshot, _chunk(tmp_path))
    assert result.period_events == []
    assert client.generate_calls == 3


@pytest.mark.parametrize(
    ("error", "reason"),
    [
        (_api_error(400, "INVALID_ARGUMENT"), BreakerReason.API_ERROR),
        (
            _api_error(429, "RESOURCE_EXHAUSTED", "GenerateRequestsPerDayPerModel-FreeTier"),
            BreakerReason.QUOTA_EXHAUSTED,
        ),
    ],
)
def test_pass1_non_retryable_error_is_not_retried_and_blocks_later_chunks(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
    error: Exception,
    reason: BreakerReason,
) -> None:
    client = _RecordingGeminiClient(_EMPTY_PASS1_JSON, errors=[error])
    analyzer = GeminiPass1Analyzer(api_key="k", client=client, resilience=_fast_resilience())
    chunk = _chunk(tmp_path)

    with job_scope(GeminiJobScope()):
        with pytest.raises(GeminiCallBlockedError) as exc_info:
            analyzer.analyze_chunk(sample_snapshot, chunk)
        assert exc_info.value.reason is reason
        assert client.generate_calls == 1, "the previous 3-attempt blanket retry is gone"

        uploads_before = client.upload_calls
        with pytest.raises(GeminiCallBlockedError):
            analyzer.analyze_chunk(sample_snapshot, chunk)
        assert client.generate_calls == 1
        assert client.upload_calls == uploads_before, "an open breaker also stops file uploads"


def test_pass2_resolver_uses_resilience_and_stops_calling_when_breaker_is_open() -> None:
    client = _RecordingGeminiClient(
        _CAP_JSON, errors=[_api_error(429, "RESOURCE_EXHAUSTED", "RequestsPerDay")]
    )
    resolver = GeminiCropGridCapResolver(
        api_key="k", client=client, resilience=_fast_resilience("pass2")
    )
    with job_scope(GeminiJobScope()):
        with pytest.raises(GeminiCallBlockedError) as first:
            resolver.resolve(_selected_crops(), allowed_cap_numbers=[7])
        assert first.value.reason is BreakerReason.QUOTA_EXHAUSTED
        with pytest.raises(GeminiCallBlockedError):
            resolver.resolve(_selected_crops(), allowed_cap_numbers=[7])
    assert client.generate_calls == 1


def test_gemini_calls_count_against_the_per_job_call_budget(
    sample_snapshot: MatchSnapshot,
    tmp_path: Path,
) -> None:
    client = _RecordingGeminiClient(_EMPTY_PASS1_JSON)
    analyzer = GeminiPass1Analyzer(api_key="k", client=client, resilience=_fast_resilience())
    chunk = _chunk(tmp_path)
    with job_scope(GeminiJobScope(max_calls=2)):
        analyzer.analyze_chunk(sample_snapshot, chunk)
        analyzer.analyze_chunk(sample_snapshot, chunk)
        with pytest.raises(GeminiCallBlockedError) as exc_info:
            analyzer.analyze_chunk(sample_snapshot, chunk)
    assert exc_info.value.reason is BreakerReason.BUDGET_EXCEEDED
    assert client.generate_calls == 2
