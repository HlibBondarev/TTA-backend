"""Unit tests for FFmpegClient even-height normalization, timeouts, and cancellation."""

from __future__ import annotations

import asyncio
import io
import json
import socket
import subprocess
import sys
import threading
import time
import traceback
from contextlib import contextmanager
from pathlib import Path

import httpx
import numpy as np
import pytest

from video_analyzer.media.ffmpeg_client import (
    FFmpegCancelledError,
    FFmpegClient,
    VideoProbeResult,
    _RawFrameStream,
    compute_keyframe_interval,
)
from video_analyzer.schemas.request import VideoSource


def _probe(duration_sec: float = 10.0, has_audio: bool = False) -> VideoProbeResult:
    return VideoProbeResult(
        duration_sec=duration_sec,
        width=1920,
        height=1080,
        fps=30.0,
        has_audio=has_audio,
        codec_name="h264",
    )


def _recording_client(
    monkeypatch: pytest.MonkeyPatch,
    encoder_listing: str = "",
    failing_encoders: tuple[str, ...] = (),
    **client_kwargs,
) -> tuple[FFmpegClient, list[list[str]]]:
    """Create a client whose ffmpeg subprocess calls are recorded instead of executed."""
    recorded_cmds: list[list[str]] = []

    def fake_run_command(cmd, stage_name, **kwargs):
        recorded_cmds.append(cmd)
        if "-encoders" in cmd:
            return subprocess.CompletedProcess(
                args=cmd, returncode=0, stdout=encoder_listing, stderr=""
            )
        if "-c:v" in cmd and cmd[cmd.index("-c:v") + 1] in failing_encoders:
            raise RuntimeError("ffmpeg pass1 mezzanine failed (exit 1): no capable device")
        return subprocess.CompletedProcess(args=cmd, returncode=0, stdout="", stderr="")

    client = FFmpegClient(**client_kwargs)
    monkeypatch.setattr(client, "_run_command", fake_run_command)
    return client, recorded_cmds


def _mezzanine_commands(cmds: list[list[str]]) -> list[list[str]]:
    return [cmd for cmd in cmds if "-vf" in cmd]


def test_create_pass1_chunks_normalizes_odd_target_height_and_preserves_even(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, recorded_cmds = _recording_client(monkeypatch)
    video_path = tmp_path / "match.mp4"
    video_path.write_bytes(b"video")

    client.create_pass1_chunks(
        video_path=video_path,
        probe=_probe(),
        output_dir=tmp_path / "odd",
        target_height=721,
        fps=2.0,
    )
    client.create_pass1_chunks(
        video_path=video_path,
        probe=_probe(),
        output_dir=tmp_path / "even",
        target_height=720,
        fps=2.0,
    )

    odd_cmd, even_cmd = _mezzanine_commands(recorded_cmds)
    # fps must come BEFORE scale: decimate 30/60 fps 4K to 2 fps first, then downscale only
    # the surviving frames (scale-first was ~4x slower on the real 4K HEVC clip).
    assert odd_cmd[odd_cmd.index("-vf") + 1] == "fps=2.0,scale=-2:720"
    assert even_cmd[even_cmd.index("-vf") + 1] == "fps=2.0,scale=-2:720"

    tiny_probe = _probe()
    tiny_dir = tmp_path / "tiny"
    with pytest.raises(ValueError, match="target_height"):
        client.create_pass1_chunks(
            video_path=video_path,
            probe=tiny_probe,
            output_dir=tiny_dir,
            target_height=1,
        )


def test_mezzanine_decodes_source_once_and_slices_chunks_with_stream_copy(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, cmds = _recording_client(monkeypatch)
    video_path = tmp_path / "match4k.mp4"
    timings: dict[str, float] = {}

    chunks = client.create_pass1_chunks(
        video_path=video_path,
        probe=_probe(duration_sec=300.0),
        output_dir=tmp_path / "chunks",
        chunk_duration_sec=120,
        overlap_sec=15,
        timings=timings,
    )

    source_reads = [cmd for cmd in cmds if str(video_path) in cmd]
    assert len(source_reads) == 1, "the source must be decoded exactly once"
    assert source_reads[0] is _mezzanine_commands(cmds)[0]
    slice_cmds = [cmd for cmd in cmds if "-c" in cmd and cmd[cmd.index("-c") + 1] == "copy"]
    assert len(slice_cmds) == len(chunks) == 3
    assert all(str(video_path) not in cmd and "-vf" not in cmd for cmd in slice_cmds)
    assert [chunk.start_sec for chunk in chunks] == [0.0, 105.0, 210.0]
    assert [chunk.end_sec for chunk in chunks] == [120.0, 225.0, 300.0]
    assert set(timings) == {"pass1Mezzanine", "pass1ChunkSlicing"}


def test_mezzanine_forces_keyframes_that_divide_chunk_overlap_and_step(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, cmds = _recording_client(monkeypatch)
    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4",
        probe=_probe(duration_sec=300.0),
        output_dir=tmp_path / "chunks",
        chunk_duration_sec=120,
        overlap_sec=15,
        fps=2.0,
    )
    mezzanine = _mezzanine_commands(cmds)[0]
    expression = mezzanine[mezzanine.index("-force_key_frames") + 1]
    interval = int(expression.rsplit("*", 1)[1].rstrip(")"))
    assert 1 <= interval <= 5
    assert 120 % interval == 0
    assert 15 % interval == 0
    assert (120 - 15) % interval == 0
    assert mezzanine[mezzanine.index("-g") + 1] == str(interval * 2)
    assert mezzanine[mezzanine.index("-bf") + 1] == "0", "B-frame delay would offset -c copy chunks"

    # Slice start times must all fall on the keyframe grid so -c copy cuts cleanly.
    starts = [float(cmd[cmd.index("-ss") + 1]) for cmd in cmds if "-c" in cmd]
    assert starts
    assert all(start % interval == 0 for start in starts)


@pytest.mark.parametrize(
    ("chunk", "overlap", "expected"),
    [(120, 15, 5), (120, 30, 5), (60, 0, 5), (84, 12, 4), (77, 7, 1), (100, 10, 5)],
)
def test_compute_keyframe_interval_divides_all_boundaries(
    chunk: int, overlap: int, expected: int
) -> None:
    interval = compute_keyframe_interval(chunk, overlap)
    assert interval == expected
    assert chunk % interval == 0
    assert overlap % interval == 0
    assert (chunk - overlap) % interval == 0


def test_mezzanine_keeps_16khz_mono_audio_only_when_source_has_audio(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, cmds = _recording_client(monkeypatch)
    for name, has_audio in (("with_audio", True), ("silent", False)):
        client.create_pass1_chunks(
            video_path=tmp_path / "m.mp4",
            probe=_probe(has_audio=has_audio),
            output_dir=tmp_path / name,
        )
    with_audio, silent = _mezzanine_commands(cmds)
    start = with_audio.index("-c:a")
    assert with_audio[start : start + 8] == [
        "-c:a",
        "aac",
        "-ac",
        "1",
        "-ar",
        "16000",
        "-b:a",
        "32k",
    ]
    assert "-an" not in with_audio
    assert "-an" in silent
    assert "-c:a" not in silent


def test_hwaccel_flag_is_configurable_and_disabled_with_none(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    for hwaccel, expect_flag in (("auto", True), ("none", False)):
        client, cmds = _recording_client(monkeypatch, hwaccel=hwaccel)
        client.create_pass1_chunks(
            video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / hwaccel
        )
        mezzanine = _mezzanine_commands(cmds)[0]
        assert ("-hwaccel" in mezzanine) is expect_flag
        if expect_flag:
            assert mezzanine[mezzanine.index("-hwaccel") + 1] == "auto"
            assert mezzanine.index("-hwaccel") < mezzanine.index("-i")


_ENCODER_LISTING = """Encoders:
 V..... libx264              libx264 H.264 / AVC
 V....D h264_qsv             H.264 (Intel Quick Sync Video acceleration)
 V....D h264_amf             AMD AMF H.264 Encoder
 A....D aac                  AAC (Advanced Audio Coding)
"""


def test_encoder_auto_detection_prefers_hardware_in_priority_order(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, cmds = _recording_client(monkeypatch, encoder_listing=_ENCODER_LISTING)
    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "o"
    )
    mezzanine = _mezzanine_commands(cmds)[0]
    assert mezzanine[mezzanine.index("-c:v") + 1] == "h264_qsv"  # nvenc absent, qsv before amf


def test_encoder_runtime_failure_falls_back_to_libx264_and_is_remembered(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    listing = " V....D h264_nvenc NVIDIA NVENC H.264\n V..... libx264 libx264\n"
    client, cmds = _recording_client(
        monkeypatch, encoder_listing=listing, failing_encoders=("h264_nvenc",)
    )
    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "a"
    )
    encoders_used = [cmd[cmd.index("-c:v") + 1] for cmd in _mezzanine_commands(cmds)]
    assert encoders_used == ["h264_nvenc", "libx264"]

    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "b"
    )
    encoders_used = [cmd[cmd.index("-c:v") + 1] for cmd in _mezzanine_commands(cmds)]
    assert encoders_used == ["h264_nvenc", "libx264", "libx264"]
    assert sum("-encoders" in cmd for cmd in cmds) == 1


def test_libx264_failure_and_cancellation_are_not_masked_by_fallback(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, _ = _recording_client(
        monkeypatch, failing_encoders=("libx264",), video_encoder="libx264"
    )
    source_path = tmp_path / "m.mp4"
    probe = _probe()
    failing_dir = tmp_path / "x"
    with pytest.raises(RuntimeError, match="no capable device"):
        client.create_pass1_chunks(video_path=source_path, probe=probe, output_dir=failing_dir)

    cancelling = FFmpegClient(video_encoder="h264_nvenc")
    calls: list[list[str]] = []

    def cancelled(cmd, stage_name, **kwargs):
        calls.append(cmd)
        raise FFmpegCancelledError("cancelled while running")

    monkeypatch.setattr(cancelling, "_run_command", cancelled)
    cancelled_dir = tmp_path / "y"
    with pytest.raises(FFmpegCancelledError):
        cancelling.create_pass1_chunks(
            video_path=source_path, probe=probe, output_dir=cancelled_dir
        )
    assert len(calls) == 1


def test_compute_chunk_windows_backs_up_short_tail_to_a_full_chunk() -> None:
    # 230s: natural windows (0,120), (105,225), (210,230); the 20s tail is < 50% of a chunk.
    windows = FFmpegClient.compute_chunk_windows(230.0, 120, 15)
    assert windows == [(0.0, 120.0), (105.0, 225.0), (110.0, 230.0)]
    assert windows[-1][1] - windows[-1][0] == 120.0

    # A tail of at least half a chunk is left untouched.
    assert FFmpegClient.compute_chunk_windows(290.0, 120, 15) == [
        (0.0, 120.0),
        (105.0, 225.0),
        (210.0, 290.0),
    ]


def test_compute_chunk_windows_aligns_backed_up_start_to_keyframe_grid() -> None:
    windows = FFmpegClient.compute_chunk_windows(233.0, 120, 15, align_sec=5.0)
    assert windows[-1] == (110.0, 233.0)
    assert windows[-1][0] % 5.0 == 0.0


def test_compute_chunk_windows_never_emits_duplicate_or_subsumed_windows() -> None:
    for total in (225.0, 240.0, 241.0, 130.0, 121.0, 1000.0, 119.5):
        windows = FFmpegClient.compute_chunk_windows(total, 120, 15)
        assert len(windows) == len(set(windows))
        assert all(start >= 0.0 for start, _ in windows)
        assert windows[-1][1] == pytest.approx(total)
        assert [w[0] for w in windows] == sorted(w[0] for w in windows)
        # No window is fully contained in another one.
        for i, (a_start, a_end) in enumerate(windows):
            for j, (b_start, b_end) in enumerate(windows):
                assert i == j or not (b_start <= a_start and a_end <= b_end)


def test_compute_chunk_windows_clamps_start_and_handles_short_clips() -> None:
    assert FFmpegClient.compute_chunk_windows(0.0, 120, 15) == []
    assert FFmpegClient.compute_chunk_windows(45.5, 120, 15) == [(0.0, 45.5)]
    assert FFmpegClient.compute_chunk_windows(120.0, 120, 15) == [(0.0, 120.0)]
    windows = FFmpegClient.compute_chunk_windows(130.0, 120, 100)
    assert all(start >= 0.0 for start, _ in windows)
    assert FFmpegClient.compute_chunk_windows(60.0, 60, 5, align_sec=5.0) == [(0.0, 60.0)]


class _FakeStreamProcess:
    """Stand-in for subprocess.Popen producing a fixed raw video payload on stdout."""

    def __init__(self, payload: bytes, returncode: int = 0, stderr: bytes = b"") -> None:
        self.stdout = io.BytesIO(payload)
        self.stderr = io.BytesIO(stderr)
        self.returncode = returncode

    def poll(self) -> int:
        return self.returncode

    def wait(self, timeout: float | None = None) -> int:
        return self.returncode

    def terminate(self) -> None:
        return None

    def kill(self) -> None:
        return None

    def communicate(self, timeout: float | None = None) -> tuple[bytes, bytes]:
        return b"", b""


def _raw_frames(count: int, width: int, height: int) -> bytes:
    frames = [np.full((height, width, 3), 10 * (i + 1), dtype=np.uint8) for i in range(count)]
    return b"".join(frame.tobytes() for frame in frames)


def test_extract_window_frames_streams_rawvideo_without_temp_files(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    captured: dict[str, object] = {}

    def fake_popen(cmd, **kwargs):
        captured["cmd"] = cmd
        return _FakeStreamProcess(_raw_frames(5, 64, 36) + b"partial-tail")

    monkeypatch.setattr("video_analyzer.media.ffmpeg_client.subprocess.Popen", fake_popen)
    monkeypatch.chdir(tmp_path)
    client = FFmpegClient(hwaccel="auto")

    frames = client.extract_window_frames(
        video_path=Path("match.mp4"),
        center_sec=10.0,
        half_window_sec=2.5,
        total_duration_sec=60.0,
        sample_fps=4.0,
        frame_width=64,
        frame_height=36,
        detector_input_width=32,
    )

    cmd = captured["cmd"]
    assert isinstance(cmd, list)
    assert cmd[cmd.index("-hwaccel") + 1] == "auto"
    assert cmd[cmd.index("-f") + 1] == "rawvideo"
    assert cmd[cmd.index("-pix_fmt") + 1] == "bgr24"
    assert cmd[-1] == "pipe:1"
    assert cmd[cmd.index("-vf") + 1] == "fps=4.0"
    assert list(tmp_path.iterdir()) == [], "Pass 2 must not write temporary files"

    assert len(frames) == 5, "a truncated trailing frame is dropped"
    assert [round(f.video_time_sec, 2) for f in frames] == [7.5, 7.75, 8.0, 8.25, 8.5]
    first = frames[0]
    assert first.image_bgr.shape == (18, 32, 3)
    assert (first.native_width, first.native_height) == (64, 36)
    assert first.scale_x == pytest.approx(2.0)
    assert first.scale_y == pytest.approx(2.0)
    native = first.native_bgr()
    assert native.shape == (36, 64, 3)
    assert abs(int(native.mean()) - 10) <= 2


def test_extract_window_frames_keeps_native_frames_when_not_downscaled(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "video_analyzer.media.ffmpeg_client.subprocess.Popen",
        lambda cmd, **kwargs: _FakeStreamProcess(_raw_frames(2, 32, 18)),
    )
    frames = FFmpegClient(hwaccel="none").extract_window_frames(
        video_path=Path("m.mp4"),
        center_sec=1.0,
        half_window_sec=1.0,
        total_duration_sec=10.0,
        sample_fps=2.0,
        frame_width=32,
        frame_height=18,
        detector_input_width=1280,
    )
    assert len(frames) == 2
    assert frames[0].native_jpeg is None
    assert frames[0].native_bgr() is frames[0].image_bgr
    assert (frames[0].scale_x, frames[0].scale_y) == (1.0, 1.0)


def test_extract_window_frames_omits_hwaccel_when_disabled_and_reports_failures(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    commands: list[list[str]] = []

    def failing_popen(cmd, **kwargs):
        commands.append(cmd)
        return _FakeStreamProcess(b"", returncode=1, stderr=b"decoder exploded")

    monkeypatch.setattr("video_analyzer.media.ffmpeg_client.subprocess.Popen", failing_popen)
    client = FFmpegClient(hwaccel="none")
    common = {
        "video_path": Path("m.mp4"),
        "center_sec": 1.0,
        "half_window_sec": 1.0,
        "total_duration_sec": 10.0,
        "sample_fps": 2.0,
        "frame_width": 32,
        "frame_height": 18,
    }
    with pytest.raises(RuntimeError, match="decoder exploded"):
        client.extract_window_frames(**common)
    assert "-hwaccel" not in commands[0]

    cancel_event = threading.Event()
    cancel_event.set()
    with pytest.raises(FFmpegCancelledError, match="before subprocess start"):
        client.extract_window_frames(**common, cancel_event=cancel_event)


def test_run_command_enforces_timeout_and_active_cancellation() -> None:
    sleep_cmd = [sys.executable, "-c", "import time; time.sleep(30)"]

    started = time.monotonic()
    with pytest.raises(TimeoutError, match="timed out"):
        FFmpegClient._run_command(
            sleep_cmd,
            "slow ffmpeg",
            timeout_sec=0.25,
            poll_interval_sec=0.05,
        )
    assert time.monotonic() - started < 3.0

    cancel_event = threading.Event()
    timer = threading.Timer(0.15, cancel_event.set)
    timer.start()
    try:
        cancel_started = time.monotonic()
        with pytest.raises(FFmpegCancelledError, match="cancelled while running"):
            FFmpegClient._run_command(
                sleep_cmd,
                "cancellable ffmpeg",
                timeout_sec=10.0,
                cancel_event=cancel_event,
                poll_interval_sec=0.05,
            )
        assert time.monotonic() - cancel_started < 3.0
    finally:
        timer.cancel()

    pre_cancelled = threading.Event()
    pre_cancelled.set()
    with pytest.raises(FFmpegCancelledError, match="before subprocess start"):
        FFmpegClient._run_command(
            [sys.executable, "-c", "print('never')"],
            "pre-cancelled",
            cancel_event=pre_cancelled,
        )

    with pytest.raises(RuntimeError, match="not found on PATH"):
        FFmpegClient._run_command(["definitely_missing_ffmpeg_binary_xyz"], "missing bin")

    with pytest.raises(RuntimeError, match="exit 3"):
        FFmpegClient._run_command(
            [sys.executable, "-c", "import sys; sys.stderr.write('boom'); sys.exit(3)"],
            "failing cmd",
        )


def test_resolve_source_path_cancels_streaming_download_closes_response_and_cleans_partial_file(
    tmp_path: Path,
) -> None:
    cancel_event = threading.Event()
    recorded_timeouts: list[object] = []

    class CancellingStream(httpx.AsyncByteStream):
        def __init__(self) -> None:
            self.chunks_yielded = 0
            self.closed = False

        async def __aiter__(self):
            self.chunks_yielded += 1
            yield b"partial-chunk-1"
            cancel_event.set()
            self.chunks_yielded += 1
            yield b"chunk-2-should-not-complete"
            self.chunks_yielded += 1
            yield b"chunk-3-should-never-be-pulled"

        async def aclose(self) -> None:
            self.closed = True

    active_stream = CancellingStream()

    def stream_handler(request: httpx.Request) -> httpx.Response:
        recorded_timeouts.append(request.extensions.get("timeout"))
        return httpx.Response(200, stream=active_stream, request=request)

    http_client = httpx.AsyncClient(transport=httpx.MockTransport(stream_handler))
    try:
        client = FFmpegClient(
            download_read_timeout_sec=5.0,
            download_total_timeout_sec=30.0,
            http_client=http_client,
        )
        source = VideoSource(kind="presigned_url", uri="https://cdn.tta.example/v.mp4?sig=abc")
        work_dir = tmp_path / "dl_cancel"
        work_dir.mkdir()

        with pytest.raises(FFmpegCancelledError, match="cancelled"):
            client.resolve_source_path(source, work_dir, cancel_event=cancel_event)

        assert active_stream.closed is True
        assert active_stream.chunks_yielded == 2
        assert not (work_dir / "source_video.mp4").exists()
        assert len(recorded_timeouts) == 1
        timeout_ext = recorded_timeouts[0]
        assert isinstance(timeout_ext, dict)
        assert timeout_ext["read"] == pytest.approx(5.0)
        assert timeout_ext["connect"] == pytest.approx(5.0)
    finally:
        asyncio.run(http_client.aclose())

    # Blocked read timing out after cancellation also maps to FFmpegCancelledError cleanly
    blocked_cancel = threading.Event()

    class BlockedReadStream(httpx.AsyncByteStream):
        def __init__(self, request: httpx.Request) -> None:
            self._request = request
            self.closed = False

        async def __aiter__(self):
            yield b"partial-before-timeout"
            blocked_cancel.set()
            raise httpx.ReadTimeout(
                f"read timed out for {self._request.url}",
                request=self._request,
            )

        async def aclose(self) -> None:
            self.closed = True

    blocked_streams: list[BlockedReadStream] = []

    def blocked_handler(request: httpx.Request) -> httpx.Response:
        stream = BlockedReadStream(request)
        blocked_streams.append(stream)
        return httpx.Response(200, stream=stream, request=request)

    blocked_http = httpx.AsyncClient(transport=httpx.MockTransport(blocked_handler))
    try:
        client = FFmpegClient(download_read_timeout_sec=1.0, http_client=blocked_http)
        work_dir = tmp_path / "dl_timeout_cancel"
        work_dir.mkdir()
        signed_url = "https://cdn.tta.example/v.mp4?X-Amz-Signature=secret-read-sig"
        with pytest.raises(FFmpegCancelledError) as exc_info:
            client.resolve_source_path(
                VideoSource(kind="presigned_url", uri=signed_url),
                work_dir,
                cancel_event=blocked_cancel,
            )
        tb_text = "".join(traceback.format_exception(exc_info.value))
        assert "secret-read-sig" not in tb_text
        assert blocked_streams[0].closed is True
        assert not (work_dir / "source_video.mp4").exists()
    finally:
        asyncio.run(blocked_http.aclose())

    # Pre-cancelled event aborts before issuing an HTTP request
    requests_sent = 0

    def never_called_handler(request: httpx.Request) -> httpx.Response:
        nonlocal requests_sent
        requests_sent += 1
        return httpx.Response(200, request=request)

    pre_http = httpx.AsyncClient(transport=httpx.MockTransport(never_called_handler))
    try:
        pre_cancel = threading.Event()
        pre_cancel.set()
        client = FFmpegClient(http_client=pre_http)
        with pytest.raises(FFmpegCancelledError):
            client.resolve_source_path(
                VideoSource(kind="presigned_url", uri="https://cdn.tta.example/v.mp4?sig=xyz"),
                tmp_path,
                cancel_event=pre_cancel,
            )
        assert requests_sent == 0
    finally:
        asyncio.run(pre_http.aclose())


def test_resolve_source_path_sanitizes_http_errors_without_altering_signed_url(
    tmp_path: Path,
) -> None:
    signed_url = (
        "https://cdn.tta.example/match.mp4?X-Amz-Signature=super-secret-sig&token=secret-tok"
    )
    received_urls: list[str] = []

    def forbidden_handler(request: httpx.Request) -> httpx.Response:
        received_urls.append(str(request.url))
        return httpx.Response(403, request=request, text="Forbidden")

    http_client = httpx.AsyncClient(transport=httpx.MockTransport(forbidden_handler))
    try:
        client = FFmpegClient(http_client=http_client)
        work_dir = tmp_path / "dl_403"
        work_dir.mkdir()
        with pytest.raises(RuntimeError) as exc_info:
            client.resolve_source_path(
                VideoSource(kind="presigned_url", uri=signed_url),
                work_dir,
            )

        assert received_urls == [signed_url]
        assert "HTTP 403" in str(exc_info.value)
        assert exc_info.value.__cause__ is None
        assert exc_info.value.__suppress_context__ is True
        tb_text = "".join(traceback.format_exception(exc_info.value))
        assert "super-secret-sig" not in tb_text
        assert "secret-tok" not in tb_text
        assert "cdn.tta.example" not in tb_text
        assert not (work_dir / "source_video.mp4").exists()
    finally:
        asyncio.run(http_client.aclose())


@contextmanager
def _slow_header_http_server():
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.bind(("127.0.0.1", 0))
    srv.listen(1)
    srv.settimeout(3.0)
    port = srv.getsockname()[1]
    headers_started = threading.Event()
    peer_closed = threading.Event()
    stop_event = threading.Event()

    def serve() -> None:
        conn: socket.socket | None = None
        try:
            conn, _ = srv.accept()
            conn.settimeout(1.0)
            conn.recv(4096)
            conn.sendall(b"HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\n")
            headers_started.set()
            while not stop_event.is_set():
                time.sleep(0.05)
                try:
                    conn.sendall(b"X-Slow-Header: 1\r\n")
                except OSError:
                    peer_closed.set()
                    break
        except TimeoutError:
            pass
        finally:
            if conn is not None:
                conn.close()
            srv.close()

    thread = threading.Thread(target=serve, daemon=True)
    thread.start()
    try:
        yield port, headers_started, peer_closed
    finally:
        stop_event.set()
        thread.join(timeout=2.0)


def test_resolve_source_path_interrupts_slow_headers_on_cancel_and_total_deadline(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    for var in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"):
        monkeypatch.delenv(var, raising=False)

    # Case A: Cooperative cancel aborts slow-header wait far before read/total timeouts
    cancel_event = threading.Event()
    with _slow_header_http_server() as (port, headers_started, peer_closed):
        timer = threading.Timer(0.15, lambda: headers_started.wait(2.0) and cancel_event.set())
        timer.start()
        try:
            client = FFmpegClient(
                download_read_timeout_sec=10.0,
                download_total_timeout_sec=300.0,
            )
            work_dir = tmp_path / "slow_hdr_cancel"
            work_dir.mkdir()
            signed_url = f"http://127.0.0.1:{port}/video.mp4?X-Amz-Signature=slow-hdr-secret"
            started = time.monotonic()
            with pytest.raises(FFmpegCancelledError) as exc_info:
                client.resolve_source_path(
                    VideoSource(kind="presigned_url", uri=signed_url),
                    work_dir,
                    cancel_event=cancel_event,
                )
            elapsed = time.monotonic() - started
            assert elapsed < 2.0
            assert peer_closed.wait(timeout=2.0) is True
            assert not (work_dir / "source_video.mp4").exists()
            tb_text = "".join(traceback.format_exception(exc_info.value))
            assert "slow-hdr-secret" not in tb_text
        finally:
            timer.cancel()

    # Case B: Total deadline stops trickling headers that defeat per-read inactivity timeouts
    with _slow_header_http_server() as (port, headers_started, peer_closed):
        client = FFmpegClient(
            download_read_timeout_sec=5.0,
            download_total_timeout_sec=0.35,
        )
        work_dir = tmp_path / "slow_hdr_deadline"
        work_dir.mkdir()
        started = time.monotonic()
        with pytest.raises(TimeoutError, match="exceeded total timeout"):
            client.resolve_source_path(
                VideoSource(kind="presigned_url", uri=f"http://127.0.0.1:{port}/video.mp4"),
                work_dir,
            )
        assert headers_started.is_set()
        assert time.monotonic() - started < 2.0
        assert peer_closed.wait(timeout=2.0) is True
        assert not (work_dir / "source_video.mp4").exists()


def test_ffmpeg_client_rejects_sync_http_client_and_interrupts_injected_async_slow_headers(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    for var in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"):
        monkeypatch.delenv(var, raising=False)

    with httpx.Client() as sync_client:
        with pytest.raises(TypeError, match="AsyncClient"):
            FFmpegClient(http_client=sync_client)  # type: ignore[arg-type]

    injected_cancel = threading.Event()
    injected_client = httpx.AsyncClient(trust_env=False)
    try:
        with _slow_header_http_server() as (port, headers_started, peer_closed):
            timer = threading.Timer(
                0.15,
                lambda: headers_started.wait(2.0) and injected_cancel.set(),
            )
            timer.start()
            try:
                client = FFmpegClient(
                    download_read_timeout_sec=10.0,
                    download_total_timeout_sec=300.0,
                    http_client=injected_client,
                )
                work_dir = tmp_path / "injected_async_slow_hdr"
                work_dir.mkdir()
                started = time.monotonic()
                with pytest.raises(FFmpegCancelledError):
                    client.resolve_source_path(
                        VideoSource(
                            kind="presigned_url",
                            uri=f"http://127.0.0.1:{port}/video.mp4?sig=slow-async-secret",
                        ),
                        work_dir,
                        cancel_event=injected_cancel,
                    )
                assert time.monotonic() - started < 2.0
                assert peer_closed.wait(timeout=2.0) is True
                assert not (work_dir / "source_video.mp4").exists()
            finally:
                timer.cancel()
    finally:
        asyncio.run(injected_client.aclose())


def test_encoder_chain_tries_next_hardware_encoder_before_libx264(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    listing = (
        " V....D h264_nvenc NVIDIA NVENC\n V..... h264_qsv Intel QSV\n"
        " V....D h264_amf AMD AMF\n V..... libx264 libx264\n"
    )
    client, cmds = _recording_client(
        monkeypatch, encoder_listing=listing, failing_encoders=("h264_nvenc",)
    )
    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "a"
    )
    used = [cmd[cmd.index("-c:v") + 1] for cmd in _mezzanine_commands(cmds)]
    assert used == ["h264_nvenc", "h264_qsv"], "QSV is tried after NVENC fails, not libx264"

    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "b"
    )
    used = [cmd[cmd.index("-c:v") + 1] for cmd in _mezzanine_commands(cmds)]
    assert used == ["h264_nvenc", "h264_qsv", "h264_qsv"], "failed encoders are not retried"

    failing_all, cmds = _recording_client(
        monkeypatch,
        encoder_listing=listing,
        failing_encoders=("h264_nvenc", "h264_qsv", "h264_amf"),
    )
    failing_all.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "c"
    )
    used = [cmd[cmd.index("-c:v") + 1] for cmd in _mezzanine_commands(cmds)]
    assert used == ["h264_nvenc", "h264_qsv", "h264_amf", "libx264"]


def test_encoder_candidates_are_kept_when_every_encoder_fails_on_a_bad_source(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    listing = (
        " V....D h264_nvenc NVIDIA NVENC\n V..... h264_qsv Intel QSV\n V..... libx264 libx264\n"
    )
    client, cmds = _recording_client(
        monkeypatch,
        encoder_listing=listing,
        failing_encoders=("h264_nvenc", "h264_qsv", "libx264"),
    )
    probe = _probe()
    failing_dir = tmp_path / "a"
    with pytest.raises(RuntimeError, match="no capable device"):
        client.create_pass1_chunks(
            video_path=tmp_path / "bad.mp4", probe=probe, output_dir=failing_dir
        )

    used = [cmd[cmd.index("-c:v") + 1] for cmd in _mezzanine_commands(cmds)]
    assert used == ["h264_nvenc", "h264_qsv", "libx264"]
    assert client._video_encoder_candidates(None) == ["h264_nvenc", "h264_qsv", "libx264"]


def test_extraction_threads_and_hwaccel_are_independent_of_the_pass1_settings(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    commands: list[list[str]] = []

    def fake_popen(cmd, **kwargs):
        commands.append(cmd)
        return _FakeStreamProcess(_raw_frames(1, 32, 18))

    monkeypatch.setattr("video_analyzer.media.ffmpeg_client.subprocess.Popen", fake_popen)
    common = {
        "video_path": Path("m.mp4"),
        "center_sec": 1.0,
        "half_window_sec": 1.0,
        "total_duration_sec": 10.0,
        "sample_fps": 2.0,
        "frame_width": 32,
        "frame_height": 18,
    }

    split = FFmpegClient(hwaccel="auto", extraction_hwaccel="none", extraction_threads=2)
    split.extract_window_frames(**common)
    assert "-hwaccel" not in commands[0]
    assert commands[0][commands[0].index("-threads") + 1] == "2"
    assert commands[0].index("-threads") < commands[0].index("-i")
    assert split._hwaccel_args() == ["-hwaccel", "auto"], "Pass 1 keeps hardware decode"

    inherited = FFmpegClient(hwaccel="auto")
    inherited.extract_window_frames(**common)
    assert commands[1][commands[1].index("-hwaccel") + 1] == "auto"
    assert "-threads" not in commands[1], "0 threads keeps the ffmpeg default"

    explicit = FFmpegClient(hwaccel="none", extraction_hwaccel="d3d11va")
    explicit.extract_window_frames(**common)
    assert commands[2][commands[2].index("-hwaccel") + 1] == "d3d11va"


def test_raw_frame_reading_reuses_one_buffer_without_corrupting_returned_frames(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        "video_analyzer.media.ffmpeg_client.subprocess.Popen",
        lambda cmd, **kwargs: _FakeStreamProcess(_raw_frames(3, 32, 18)),
    )
    common = {
        "video_path": Path("m.mp4"),
        "center_sec": 1.0,
        "half_window_sec": 1.0,
        "total_duration_sec": 10.0,
        "sample_fps": 2.0,
        "frame_width": 32,
        "frame_height": 18,
    }
    # Native-size path (frames are copied out of the shared buffer) ...
    native_frames = FFmpegClient(hwaccel="none").extract_window_frames(**common)
    assert [int(f.image_bgr.mean()) for f in native_frames] == [10, 20, 30]
    # ... and the downscaled path (JPEG copy + detection thumbnail) both keep per-frame data.
    scaled_frames = FFmpegClient(hwaccel="none").extract_window_frames(
        **common, detector_input_width=16
    )
    assert [int(f.image_bgr.mean()) for f in scaled_frames] == [10, 20, 30]
    for frame, expected in zip(scaled_frames, (10, 20, 30), strict=True):
        assert abs(int(frame.native_bgr().mean()) - expected) <= 2


# ------------------------------------------------------- probe, mapping, timeouts, keyframes


def _ffprobe_payload(streams: list[dict], duration: str = "120.0") -> str:
    return json.dumps({"streams": streams, "format": {"duration": duration}})


def _probe_with(monkeypatch: pytest.MonkeyPatch, payload: str) -> VideoProbeResult:
    client = FFmpegClient()
    monkeypatch.setattr(
        client,
        "_run_command",
        lambda cmd, stage, **kw: subprocess.CompletedProcess(cmd, 0, stdout=payload, stderr=""),
    )
    return client.probe(Path("match.mp4"))


def test_probe_records_selected_stream_index_and_skips_attached_cover_art(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    streams = [
        {"index": 0, "codec_type": "audio", "codec_name": "aac"},
        {
            "index": 1,
            "codec_type": "video",
            "codec_name": "mjpeg",
            "width": 600,
            "height": 600,
            "disposition": {"attached_pic": 1},
        },
        {
            "index": 2,
            "codec_type": "video",
            "codec_name": "hevc",
            "width": 3840,
            "height": 2160,
            "avg_frame_rate": "30/1",
        },
    ]
    result = _probe_with(monkeypatch, _ffprobe_payload(streams))
    assert result.video_stream_index == 2
    assert (result.width, result.height, result.rotation_deg) == (3840, 2160, 0)
    assert result.has_audio is True


@pytest.mark.parametrize(
    "stream_patch",
    [
        {"side_data_list": [{"side_data_type": "Display Matrix", "rotation": -90}]},
        {"side_data_list": [{"side_data_type": "Display Matrix", "rotation": 90}]},
        {"tags": {"rotate": "270"}},
    ],
)
def test_probe_reports_display_dimensions_for_quarter_turn_rotation(
    monkeypatch: pytest.MonkeyPatch, stream_patch: dict
) -> None:
    stream = {
        "index": 0,
        "codec_type": "video",
        "codec_name": "h264",
        "width": 1920,
        "height": 1080,
    }
    result = _probe_with(monkeypatch, _ffprobe_payload([{**stream, **stream_patch}]))
    assert (result.width, result.height) == (1080, 1920), "FFmpeg auto-rotates the raw frames"
    assert result.rotation_deg in (90, 270)


def test_probe_keeps_dimensions_for_half_turn_rotation(monkeypatch: pytest.MonkeyPatch) -> None:
    stream = {
        "index": 0,
        "codec_type": "video",
        "width": 1920,
        "height": 1080,
        "side_data_list": [{"rotation": 180}],
    }
    result = _probe_with(monkeypatch, _ffprobe_payload([stream]))
    assert (result.width, result.height, result.rotation_deg) == (1920, 1080, 180)


def test_both_ffmpeg_passes_map_the_probed_video_stream_explicitly(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, cmds = _recording_client(monkeypatch)
    probe = VideoProbeResult(
        duration_sec=10.0,
        width=1920,
        height=1080,
        fps=30.0,
        has_audio=True,
        codec_name="h264",
        video_stream_index=2,
    )
    client.create_pass1_chunks(video_path=tmp_path / "m.mp4", probe=probe, output_dir=tmp_path)
    mezzanine = _mezzanine_commands(cmds)[0]
    maps = [mezzanine[i + 1] for i, arg in enumerate(mezzanine) if arg == "-map"]
    assert maps == ["0:2", "0:a:0?"]
    assert mezzanine.index("-map") > mezzanine.index("-i")

    captured: list[list[str]] = []

    def fake_popen(cmd, **kwargs):
        captured.append(cmd)
        return _FakeStreamProcess(_raw_frames(1, 32, 18))

    monkeypatch.setattr("video_analyzer.media.ffmpeg_client.subprocess.Popen", fake_popen)
    FFmpegClient(hwaccel="none").extract_window_frames(
        video_path=Path("m.mp4"),
        center_sec=1.0,
        half_window_sec=1.0,
        total_duration_sec=10.0,
        sample_fps=2.0,
        frame_width=32,
        frame_height=18,
        video_stream_index=2,
    )
    assert captured[0][captured[0].index("-map") + 1] == "0:2"


def test_unprobed_stream_index_falls_back_to_first_video_stream(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    client, cmds = _recording_client(monkeypatch)
    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4", probe=_probe(), output_dir=tmp_path / "o"
    )
    mezzanine = _mezzanine_commands(cmds)[0]
    assert mezzanine[mezzanine.index("-map") + 1] == "0:v:0"
    assert "0:a:0?" not in mezzanine, "silent sources must not map an audio stream"


def test_mezzanine_timeout_scales_with_source_duration_but_slices_stay_bounded(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    timeouts: dict[str, float] = {}

    def fake_run_command(cmd, stage_name, **kwargs):
        timeouts.setdefault(stage_name, kwargs["timeout_sec"])
        return subprocess.CompletedProcess(args=cmd, returncode=0, stdout="", stderr="")

    client = FFmpegClient(ffmpeg_timeout_sec=600.0, video_encoder="libx264")
    monkeypatch.setattr(client, "_run_command", fake_run_command)
    client.create_pass1_chunks(
        video_path=tmp_path / "m.mp4",
        probe=_probe(duration_sec=65 * 60.0),
        output_dir=tmp_path / "o",
    )
    assert timeouts["ffmpeg pass1 mezzanine"] == pytest.approx(600.0 + 65 * 60.0)
    assert timeouts["ffmpeg pass1 chunk slicing"] == pytest.approx(600.0)


def test_slice_chunks_report_the_real_first_keyframe_time_as_chunk_start(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    # With fps 0.7 forced keyframes land after the nominal 5 s grid (e.g. 5.714 s).
    keyframe_listing = "0.000000,K__\n1.428571,___\n5.714286,K__\n10.000000,K__\n10.5,___\n"
    seen: list[list[str]] = []

    def fake_run_command(cmd, stage_name, **kwargs):
        seen.append(cmd)
        stdout = keyframe_listing if "packet=pts_time,flags" in cmd else ""
        return subprocess.CompletedProcess(args=cmd, returncode=0, stdout=stdout, stderr="")

    client = FFmpegClient()
    monkeypatch.setattr(client, "_run_command", fake_run_command)
    chunks = client.slice_chunks(
        tmp_path / "mezz.mp4", [(0.0, 12.0), (6.0, 18.0), (10.0, 20.0)], tmp_path / "chunks"
    )
    assert [chunk.start_sec for chunk in chunks] == [0.0, pytest.approx(5.714286), 10.0]
    assert [chunk.end_sec for chunk in chunks] == [12.0, 18.0, 20.0]
    slice_cmds = [cmd for cmd in seen if "-c" in cmd]
    starts = [float(cmd[cmd.index("-ss") + 1]) for cmd in slice_cmds]
    assert starts == [0.0, pytest.approx(5.714286), 10.0]
    durations = [float(cmd[cmd.index("-t") + 1]) for cmd in slice_cmds]
    assert durations[1] == pytest.approx(18.0 - 5.714286, abs=1e-3)


def test_slice_chunks_keep_nominal_starts_when_keyframe_probe_fails(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    def fake_run_command(cmd, stage_name, **kwargs):
        if "packet=pts_time,flags" in cmd:
            raise RuntimeError("ffprobe failed")
        return subprocess.CompletedProcess(args=cmd, returncode=0, stdout="", stderr="")

    client = FFmpegClient()
    monkeypatch.setattr(client, "_run_command", fake_run_command)
    chunks = client.slice_chunks(tmp_path / "mezz.mp4", [(3.0, 9.0)], tmp_path / "chunks")
    assert [chunk.start_sec for chunk in chunks] == [3.0]


class _StubbornProcess:
    """Process that ignores terminate() and only dies on kill(); communicate() is forbidden."""

    def __init__(self) -> None:
        self.events: list[str] = []
        self._dead = False

    def poll(self) -> int | None:
        return 0 if self._dead else None

    def terminate(self) -> None:
        self.events.append("terminate")

    def kill(self) -> None:
        self.events.append("kill")
        self._dead = True

    def wait(self, timeout: float | None = None) -> int:
        self.events.append("wait")
        if not self._dead:
            raise subprocess.TimeoutExpired("ffmpeg", timeout or 0.0)
        return -9

    def communicate(self, timeout: float | None = None):
        raise AssertionError("communicate() would race the stdout reader and stderr drain")


def test_raw_stream_stop_process_terminates_waits_then_kills_without_communicate() -> None:
    proc = _StubbornProcess()
    _RawFrameStream._stop_process(proc)  # type: ignore[arg-type]
    assert proc.events == ["terminate", "wait", "kill", "wait"]
