"""Unit tests for FFmpegClient even-height normalization, timeouts, and cancellation."""

from __future__ import annotations

import subprocess
import sys
import threading
import time
from pathlib import Path

import pytest

from video_analyzer.media.ffmpeg_client import (
    FFmpegCancelledError,
    FFmpegClient,
    VideoProbeResult,
)


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
