"""FFmpeg/FFprobe wrapper for video inspection, Pass 1 chunking, and Pass 2 4K windows."""

from __future__ import annotations

import json
import logging
import subprocess
import threading
import time
from dataclasses import dataclass
from fractions import Fraction
from pathlib import Path
from typing import Any

import cv2
import httpx
import numpy as np

from video_analyzer.schemas.request import VideoSource

logger = logging.getLogger(__name__)

DEFAULT_FFPROBE_TIMEOUT_SEC: float = 60.0
DEFAULT_FFMPEG_TIMEOUT_SEC: float = 600.0
DEFAULT_DOWNLOAD_READ_TIMEOUT_SEC: float = 10.0
DEFAULT_DOWNLOAD_TOTAL_TIMEOUT_SEC: float = 300.0


class FFmpegCancelledError(RuntimeError):
    """Raised when a media download or FFmpeg/FFprobe subprocess is cancelled cooperatively."""


@dataclass(frozen=True, slots=True)
class VideoProbeResult:
    """Technical metadata extracted via ffprobe."""

    duration_sec: float
    width: int
    height: int
    fps: float
    has_audio: bool
    codec_name: str


@dataclass(frozen=True, slots=True)
class VideoChunk:
    """A compressed, downscaled Pass 1 video chunk with timeline offsets."""

    index: int
    start_sec: float
    end_sec: float
    path: Path


@dataclass(slots=True)
class ExtractedFrame:
    """A native-resolution frame extracted for Pass 2 tracking and cap cropping."""

    index: int
    video_time_sec: float
    image_bgr: np.ndarray


class FFmpegClient:
    """Cross-platform (Windows/Linux) FFmpeg and FFprobe media processor."""

    def __init__(
        self,
        ffmpeg_bin: str = "ffmpeg",
        ffprobe_bin: str = "ffprobe",
        ffprobe_timeout_sec: float = DEFAULT_FFPROBE_TIMEOUT_SEC,
        ffmpeg_timeout_sec: float = DEFAULT_FFMPEG_TIMEOUT_SEC,
        download_read_timeout_sec: float = DEFAULT_DOWNLOAD_READ_TIMEOUT_SEC,
        download_total_timeout_sec: float = DEFAULT_DOWNLOAD_TOTAL_TIMEOUT_SEC,
        http_client: httpx.Client | None = None,
    ) -> None:
        if ffprobe_timeout_sec <= 0:
            raise ValueError("ffprobe_timeout_sec must be positive.")
        if ffmpeg_timeout_sec <= 0:
            raise ValueError("ffmpeg_timeout_sec must be positive.")
        if download_read_timeout_sec <= 0:
            raise ValueError("download_read_timeout_sec must be positive.")
        if download_total_timeout_sec <= 0:
            raise ValueError("download_total_timeout_sec must be positive.")
        self._ffmpeg_bin = ffmpeg_bin
        self._ffprobe_bin = ffprobe_bin
        self._ffprobe_timeout_sec = ffprobe_timeout_sec
        self._ffmpeg_timeout_sec = ffmpeg_timeout_sec
        self._download_read_timeout_sec = download_read_timeout_sec
        self._download_total_timeout_sec = download_total_timeout_sec
        self._http_client = http_client

    def resolve_source_path(
        self,
        source: VideoSource,
        work_dir: Path,
        cancel_event: threading.Event | None = None,
    ) -> Path:
        """Resolve a local path or download a presigned URL once into the working directory."""
        self._check_download_cancelled(cancel_event)
        if source.kind == "local_path":
            assert source.local_path is not None
            path = Path(source.local_path)
            if not path.is_file():
                raise FileNotFoundError(f"Video file not found: {path}")
            return path

        assert source.uri is not None
        dest_path = work_dir / "source_video.mp4"
        return self._download_source_video(source.uri, dest_path, cancel_event)

    def _download_source_video(
        self,
        uri: str,
        dest_path: Path,
        cancel_event: threading.Event | None,
    ) -> Path:
        per_op_timeout = min(self._download_read_timeout_sec, self._download_total_timeout_sec)
        connect_timeout = min(per_op_timeout, 10.0)
        timeout = httpx.Timeout(
            connect=connect_timeout,
            read=per_op_timeout,
            write=connect_timeout,
            pool=connect_timeout,
        )
        deadline = time.monotonic() + self._download_total_timeout_sec
        logger.info("Downloading source video from presigned URI to %s.", dest_path)

        stream_cm = (
            self._http_client.stream(
                "GET",
                uri,
                follow_redirects=True,
                timeout=timeout,
            )
            if self._http_client is not None
            else httpx.stream(
                "GET",
                uri,
                follow_redirects=True,
                timeout=timeout,
            )
        )

        try:
            with stream_cm as response:
                self._check_download_cancelled(cancel_event)
                response.raise_for_status()
                with dest_path.open("wb") as handle:
                    for chunk in response.iter_bytes():
                        self._check_download_cancelled(cancel_event)
                        if time.monotonic() > deadline:
                            raise TimeoutError(
                                "Source video download exceeded total timeout "
                                f"of {self._download_total_timeout_sec:.1f}s."
                            )
                        handle.write(chunk)
                        self._check_download_cancelled(cancel_event)
                self._check_download_cancelled(cancel_event)
        except httpx.HTTPStatusError as exc:
            dest_path.unlink(missing_ok=True)
            status_code = exc.response.status_code
            raise RuntimeError(f"Failed to download source video (HTTP {status_code}).") from None
        except httpx.TimeoutException:
            dest_path.unlink(missing_ok=True)
            if cancel_event is not None and cancel_event.is_set():
                raise FFmpegCancelledError("Source video download cancelled.") from None
            raise TimeoutError("Source video download timed out while reading response.") from None
        except httpx.HTTPError as exc:
            dest_path.unlink(missing_ok=True)
            raise RuntimeError(
                f"Failed to download source video ({type(exc).__name__})."
            ) from None
        except BaseException:
            dest_path.unlink(missing_ok=True)
            raise
        return dest_path

    @staticmethod
    def _check_download_cancelled(cancel_event: threading.Event | None) -> None:
        if cancel_event is not None and cancel_event.is_set():
            raise FFmpegCancelledError("Source video download cancelled.")

    def probe(
        self,
        video_path: Path,
        cancel_event: threading.Event | None = None,
    ) -> VideoProbeResult:
        """Inspect video resolution, frame rate, duration, and audio presence using ffprobe."""
        cmd = [
            self._ffprobe_bin,
            "-v",
            "quiet",
            "-print_format",
            "json",
            "-show_format",
            "-show_streams",
            str(video_path),
        ]
        completed = self._run_command(
            cmd,
            "ffprobe",
            timeout_sec=self._ffprobe_timeout_sec,
            cancel_event=cancel_event,
        )
        payload: dict[str, Any] = json.loads(completed.stdout or "{}")
        streams = payload.get("streams", [])
        fmt = payload.get("format", {})

        video_stream = next((s for s in streams if s.get("codec_type") == "video"), None)
        if video_stream is None:
            raise RuntimeError(f"No video stream found in '{video_path}'.")

        has_audio = any(s.get("codec_type") == "audio" for s in streams)
        width = int(video_stream.get("width", 0))
        height = int(video_stream.get("height", 0))
        codec_name = str(video_stream.get("codec_name", "unknown"))
        fps = self._parse_frame_rate(
            video_stream.get("avg_frame_rate") or video_stream.get("r_frame_rate") or "30/1"
        )
        duration_sec = self._parse_duration(video_stream, fmt)

        return VideoProbeResult(
            duration_sec=duration_sec,
            width=width,
            height=height,
            fps=fps,
            has_audio=has_audio,
            codec_name=codec_name,
        )

    def create_pass1_chunks(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        output_dir: Path,
        chunk_duration_sec: int = 120,
        overlap_sec: int = 15,
        target_height: int = 720,
        fps: float = 2.0,
        cancel_event: threading.Event | None = None,
    ) -> list[VideoChunk]:
        """Transcode downscaled overlapping chunks (preserving audio) for Gemini Pass 1."""
        if target_height < 2:
            raise ValueError("target_height must be at least 2 pixels for yuv420p encoding.")
        output_dir.mkdir(parents=True, exist_ok=True)
        windows = self.compute_chunk_windows(
            total_duration_sec=probe.duration_sec,
            chunk_duration_sec=chunk_duration_sec,
            overlap_sec=overlap_sec,
        )

        chunks: list[VideoChunk] = []
        # yuv420p requires even width and height; keep valid even heights unchanged.
        effective_height = target_height - (target_height % 2)
        scale_filter = f"scale=-2:{effective_height},fps={fps}"
        for idx, (start_sec, end_sec) in enumerate(windows):
            chunk_len = max(0.5, end_sec - start_sec)
            chunk_path = output_dir / f"pass1_chunk_{idx:04d}.mp4"
            cmd = [
                self._ffmpeg_bin,
                "-y",
                "-ss",
                f"{start_sec:.3f}",
                "-i",
                str(video_path),
                "-t",
                f"{chunk_len:.3f}",
                "-vf",
                scale_filter,
                "-c:v",
                "libx264",
                "-preset",
                "veryfast",
                "-crf",
                "28",
                "-pix_fmt",
                "yuv420p",
            ]
            if probe.has_audio:
                cmd.extend(["-c:a", "aac", "-ac", "1", "-b:a", "64k"])
            else:
                cmd.append("-an")
            cmd.extend(["-movflags", "+faststart", str(chunk_path)])

            self._run_command(
                cmd,
                "ffmpeg pass1 chunking",
                timeout_sec=self._ffmpeg_timeout_sec,
                cancel_event=cancel_event,
            )
            chunks.append(
                VideoChunk(index=idx, start_sec=start_sec, end_sec=end_sec, path=chunk_path)
            )

        return chunks

    def extract_window_frames(
        self,
        video_path: Path,
        center_sec: float,
        half_window_sec: float,
        total_duration_sec: float,
        sample_fps: float,
        work_dir: Path,
        cancel_event: threading.Event | None = None,
    ) -> list[ExtractedFrame]:
        """Extract native-resolution frames in a narrow [t - d, t + d] window around an event."""
        work_dir.mkdir(parents=True, exist_ok=True)
        win_start = max(0.0, center_sec - half_window_sec)
        win_end = min(total_duration_sec, center_sec + half_window_sec)
        win_dur = max(0.25, win_end - win_start)

        frame_pattern = work_dir / "frame_%05d.jpg"
        cmd = [
            self._ffmpeg_bin,
            "-y",
            "-ss",
            f"{win_start:.3f}",
            "-i",
            str(video_path),
            "-t",
            f"{win_dur:.3f}",
            "-vf",
            f"fps={sample_fps}",
            "-q:v",
            "2",
            str(frame_pattern),
        ]
        self._run_command(
            cmd,
            "ffmpeg pass2 window extraction",
            timeout_sec=self._ffmpeg_timeout_sec,
            cancel_event=cancel_event,
        )

        extracted: list[ExtractedFrame] = []
        for idx, frame_path in enumerate(sorted(work_dir.glob("frame_*.jpg"))):
            image = cv2.imread(str(frame_path), cv2.IMREAD_COLOR)
            if image is None:
                logger.warning("Failed to decode extracted frame '%s'; skipping.", frame_path)
                continue
            video_time = min(total_duration_sec, win_start + (idx / sample_fps))
            extracted.append(ExtractedFrame(index=idx, video_time_sec=video_time, image_bgr=image))

        return extracted

    @staticmethod
    def compute_chunk_windows(
        total_duration_sec: float,
        chunk_duration_sec: int,
        overlap_sec: int,
    ) -> list[tuple[float, float]]:
        """Compute deterministic overlapping [start, end] time ranges covering the full video."""
        if total_duration_sec <= 0:
            return []
        if total_duration_sec <= chunk_duration_sec:
            return [(0.0, total_duration_sec)]

        step = max(1, chunk_duration_sec - overlap_sec)
        windows: list[tuple[float, float]] = []
        cursor = 0.0
        while cursor < total_duration_sec:
            end_sec = min(total_duration_sec, cursor + chunk_duration_sec)
            windows.append((round(cursor, 3), round(end_sec, 3)))
            if end_sec >= total_duration_sec:
                break
            cursor += step
        return windows

    @staticmethod
    def _parse_frame_rate(raw_rate: str) -> float:
        try:
            if "/" in raw_rate:
                value = float(Fraction(raw_rate))
                return round(value, 3) if value > 0 else 30.0
            value = float(raw_rate)
            return round(value, 3) if value > 0 else 30.0
        except (ValueError, ZeroDivisionError):
            return 30.0

    @staticmethod
    def _parse_duration(video_stream: dict[str, Any], fmt: dict[str, Any]) -> float:
        for candidate in (fmt.get("duration"), video_stream.get("duration")):
            if candidate is not None:
                try:
                    value = float(candidate)
                    if value > 0:
                        return value
                except (TypeError, ValueError):
                    continue
        raise RuntimeError("Unable to determine positive video duration from ffprobe metadata.")

    @staticmethod
    def _run_command(
        cmd: list[str],
        stage_name: str,
        *,
        timeout_sec: float = DEFAULT_FFMPEG_TIMEOUT_SEC,
        cancel_event: threading.Event | None = None,
        poll_interval_sec: float = 0.1,
    ) -> subprocess.CompletedProcess[str]:
        if cancel_event is not None and cancel_event.is_set():
            raise FFmpegCancelledError(f"{stage_name} cancelled before subprocess start.")
        try:
            proc = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
        except FileNotFoundError as exc:
            raise RuntimeError(
                f"Required binary '{cmd[0]}' for {stage_name} was not found on PATH."
            ) from exc

        deadline = time.monotonic() + timeout_sec
        stdout = ""
        stderr = ""
        with proc:
            while True:
                if cancel_event is not None and cancel_event.is_set():
                    FFmpegClient._terminate_process(proc)
                    raise FFmpegCancelledError(f"{stage_name} cancelled while running.")
                remaining = deadline - time.monotonic()
                if remaining <= 0.0:
                    FFmpegClient._terminate_process(proc)
                    raise TimeoutError(f"{stage_name} timed out after {timeout_sec:.1f}s.")
                slice_timeout = (
                    min(remaining, poll_interval_sec) if cancel_event is not None else remaining
                )
                try:
                    out, err = proc.communicate(timeout=slice_timeout)
                    stdout = out or ""
                    stderr = err or ""
                    break
                except subprocess.TimeoutExpired:
                    continue

        if proc.returncode != 0:
            stderr_tail = stderr.strip()[-600:]
            raise RuntimeError(
                f"{stage_name} failed (exit {proc.returncode}): {stderr_tail}"
            )
        return subprocess.CompletedProcess(
            args=cmd,
            returncode=proc.returncode,
            stdout=stdout,
            stderr=stderr,
        )

    @staticmethod
    def _terminate_process(proc: subprocess.Popen[str]) -> None:
        if proc.poll() is not None:
            return
        try:
            proc.terminate()
            proc.communicate(timeout=2.0)
        except subprocess.TimeoutExpired:
            proc.kill()
            try:
                proc.communicate(timeout=2.0)
            except subprocess.TimeoutExpired:
                pass
        except OSError:
            pass
