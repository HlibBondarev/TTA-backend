"""Unit tests for FFmpegClient even-height normalization, timeouts, and cancellation."""

from __future__ import annotations

import asyncio
import socket
import subprocess
import sys
import threading
import time
import traceback
from contextlib import contextmanager
from pathlib import Path

import httpx
import pytest

from video_analyzer.media.ffmpeg_client import (
    FFmpegCancelledError,
    FFmpegClient,
    VideoProbeResult,
)
from video_analyzer.schemas.request import VideoSource


def test_create_pass1_chunks_normalizes_odd_target_height_and_preserves_even(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    recorded_cmds: list[list[str]] = []

    def fake_run_command(cmd, stage_name, **kwargs):
        recorded_cmds.append(cmd)
        return subprocess.CompletedProcess(args=cmd, returncode=0, stdout="", stderr="")

    client = FFmpegClient()
    monkeypatch.setattr(client, "_run_command", fake_run_command)

    probe = VideoProbeResult(
        duration_sec=10.0,
        width=1920,
        height=1080,
        fps=30.0,
        has_audio=False,
        codec_name="h264",
    )
    video_path = tmp_path / "match.mp4"
    video_path.write_bytes(b"video")

    client.create_pass1_chunks(
        video_path=video_path,
        probe=probe,
        output_dir=tmp_path / "odd",
        target_height=721,
        fps=2.0,
    )
    client.create_pass1_chunks(
        video_path=video_path,
        probe=probe,
        output_dir=tmp_path / "even",
        target_height=720,
        fps=2.0,
    )

    odd_vf = recorded_cmds[0][recorded_cmds[0].index("-vf") + 1]
    even_vf = recorded_cmds[1][recorded_cmds[1].index("-vf") + 1]
    assert odd_vf == "scale=-2:720,fps=2.0"
    assert even_vf == "scale=-2:720,fps=2.0"

    with pytest.raises(ValueError, match="target_height"):
        client.create_pass1_chunks(
            video_path=video_path,
            probe=probe,
            output_dir=tmp_path / "tiny",
            target_height=1,
        )


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
