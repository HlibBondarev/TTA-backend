"""Tests for the 4 fps mezzanine, metadata stripping and accurate verification clip cutting."""

from __future__ import annotations

import subprocess
from pathlib import Path

import pytest

from video_analyzer.media.ffmpeg_client import (
    DEFAULT_MEZZANINE_FPS,
    PASS1_MEZZANINE_FILENAME,
    FFmpegClient,
    VideoProbeResult,
)
from video_analyzer.schemas.request import AnalysisOptions


def _probe() -> VideoProbeResult:
    return VideoProbeResult(
        duration_sec=300.0,
        width=1920,
        height=1080,
        fps=30.0,
        has_audio=True,
        codec_name="h264",
    )


def _recording_client(monkeypatch: pytest.MonkeyPatch) -> tuple[FFmpegClient, list[list[str]]]:
    recorded: list[list[str]] = []

    def fake_run_command(cmd, stage_name, **kwargs):
        recorded.append(cmd)
        stdout = ""
        return subprocess.CompletedProcess(args=cmd, returncode=0, stdout=stdout, stderr="")

    client = FFmpegClient(video_encoder="libx264")
    monkeypatch.setattr(client, "_run_command", fake_run_command)
    return client, recorded


def test_mezzanine_fps_defaults_to_four_and_is_independent_of_gemini_sampling() -> None:
    assert DEFAULT_MEZZANINE_FPS == 4.0
    assert AnalysisOptions().mezzanine_fps == 4.0


def test_default_mezzanine_is_encoded_at_four_fps_and_stays_on_disk(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    client, recorded = _recording_client(monkeypatch)
    out_dir = tmp_path / "chunks"

    client.create_pass1_chunks(
        video_path=tmp_path / "match.mp4", probe=_probe(), output_dir=out_dir
    )

    mezzanine_cmd = next(cmd for cmd in recorded if "-vf" in cmd)
    assert mezzanine_cmd[mezzanine_cmd.index("-vf") + 1] == "fps=4.0,scale=-2:720"
    assert mezzanine_cmd[-1] == str(out_dir / PASS1_MEZZANINE_FILENAME)
    assert FFmpegClient.mezzanine_path(out_dir) == out_dir / PASS1_MEZZANINE_FILENAME


def test_chunk_slicing_strips_container_metadata(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    client, recorded = _recording_client(monkeypatch)

    client.create_pass1_chunks(
        video_path=tmp_path / "match.mp4", probe=_probe(), output_dir=tmp_path / "chunks"
    )

    slice_cmds = [cmd for cmd in recorded if "-c" in cmd and cmd[cmd.index("-c") + 1] == "copy"]
    assert slice_cmds
    for cmd in slice_cmds:
        assert cmd[cmd.index("-map_metadata") + 1] == "-1"


def test_cut_verification_clip_reencodes_accurately_from_the_mezzanine(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    client, recorded = _recording_client(monkeypatch)
    mezzanine = tmp_path / "mezz.mp4"
    out_path = tmp_path / "clips" / "clip.mp4"

    result = client.cut_verification_clip(
        mezzanine_path=mezzanine, center_sec=50.0, pre_sec=3.0, post_sec=2.0, out_path=out_path
    )

    assert result == out_path
    assert out_path.parent.is_dir()
    cmd = recorded[0]
    # Input-side -ss (before -i) avoids decoding from 0:00; re-encoding keeps the cut accurate.
    assert cmd.index("-ss") < cmd.index("-i")
    assert cmd[cmd.index("-ss") + 1] == "47.000"
    assert cmd[cmd.index("-t") + 1] == "5.000"
    assert cmd[cmd.index("-c:v") + 1] == "libx264"
    assert cmd[cmd.index("-preset") + 1] == "ultrafast"
    assert cmd[cmd.index("-c:a") + 1] == "aac"
    assert "0:a:0?" in cmd
    assert cmd[cmd.index("-map_metadata") + 1] == "-1"
    assert "copy" not in cmd
    assert cmd[-1] == str(out_path)


def test_cut_verification_clip_clamps_the_start_at_zero(
    monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    client, recorded = _recording_client(monkeypatch)

    client.cut_verification_clip(
        mezzanine_path=tmp_path / "m.mp4",
        center_sec=1.0,
        pre_sec=3.0,
        post_sec=2.0,
        out_path=tmp_path / "clip.mp4",
    )

    cmd = recorded[0]
    assert cmd[cmd.index("-ss") + 1] == "0.000"
    assert cmd[cmd.index("-t") + 1] == "3.000"
