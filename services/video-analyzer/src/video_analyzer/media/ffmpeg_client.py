"""FFmpeg/FFprobe wrapper for video inspection, Pass 1 chunking, and Pass 2 4K windows."""

from __future__ import annotations

import asyncio
import json
import logging
import math
import re
import subprocess
import threading
import time
from collections.abc import Coroutine
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from fractions import Fraction
from pathlib import Path
from types import TracebackType
from typing import Any, BinaryIO

import cv2
import httpx
import numpy as np

from video_analyzer.schemas.request import VideoSource

logger = logging.getLogger(__name__)

DEFAULT_FFPROBE_TIMEOUT_SEC: float = 60.0
DEFAULT_FFMPEG_TIMEOUT_SEC: float = 600.0
DEFAULT_DOWNLOAD_READ_TIMEOUT_SEC: float = 10.0
DEFAULT_DOWNLOAD_TOTAL_TIMEOUT_SEC: float = 300.0

LIBX264_ENCODER: str = "libx264"
# Hardware encoders are preferred in this order when FFMPEG_VIDEO_ENCODER=auto.
ENCODER_PRIORITY: tuple[str, ...] = ("h264_nvenc", "h264_qsv", "h264_amf", LIBX264_ENCODER)
HWACCEL_DISABLED: str = "none"
MIN_KEYFRAME_INTERVAL_SEC: int = 1
MAX_KEYFRAME_INTERVAL_SEC: int = 5
MIN_TAIL_FRACTION: float = 0.5
NATIVE_JPEG_QUALITY: int = 95
_ENCODER_PROBE_TIMEOUT_SEC: float = 30.0
_STREAM_POLL_INTERVAL_SEC: float = 0.1
_STREAM_EXIT_TIMEOUT_SEC: float = 5.0
_STDERR_TAIL_CHARS: int = 600
_ENCODER_LINE_PATTERN = re.compile(r"^\s*V\S*\s+(\S+)")

# Per-encoder constant-quality arguments (roughly equivalent to libx264 CRF 28).
_ENCODER_QUALITY_ARGS: dict[str, list[str]] = {
    LIBX264_ENCODER: ["-preset", "veryfast", "-crf", "28"],
    "h264_nvenc": ["-preset", "p4", "-rc", "vbr", "-cq", "28", "-b:v", "0"],
    "h264_qsv": ["-preset", "veryfast", "-global_quality", "28"],
    "h264_amf": ["-quality", "speed", "-rc", "cqp", "-qp_i", "28", "-qp_p", "28"],
}


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
    """A Pass 2 frame: a detection-resolution image plus an optional compact native copy.

    ``image_bgr`` is what detectors and the tracker see. When the source was downscaled for
    detection, the original native pixels are kept only as JPEG bytes (``native_jpeg``) and are
    decoded on demand by :meth:`native_bgr`, which keeps per-window memory far below that of
    holding raw 4K frames. Without a native copy, ``image_bgr`` already is the native frame.
    """

    index: int
    video_time_sec: float
    image_bgr: np.ndarray
    native_width: int = 0
    native_height: int = 0
    native_jpeg: bytes | None = None

    def __post_init__(self) -> None:
        height, width = self.image_bgr.shape[:2]
        if self.native_width <= 0:
            self.native_width = width
        if self.native_height <= 0:
            self.native_height = height

    @property
    def scale_x(self) -> float:
        """Horizontal factor mapping detection-image pixels to native pixels."""
        return self.native_width / float(self.image_bgr.shape[1])

    @property
    def scale_y(self) -> float:
        """Vertical factor mapping detection-image pixels to native pixels."""
        return self.native_height / float(self.image_bgr.shape[0])

    def native_bgr(self) -> np.ndarray:
        """Return the native-resolution BGR frame, decoding the compact copy when present."""
        if self.native_jpeg is None:
            return self.image_bgr
        decoded = cv2.imdecode(np.frombuffer(self.native_jpeg, dtype=np.uint8), cv2.IMREAD_COLOR)
        if decoded is None:
            raise RuntimeError(f"Failed to decode native copy of Pass 2 frame {self.index}.")
        return decoded


def compute_keyframe_interval(chunk_duration_sec: int, overlap_sec: int) -> int:
    """Return the largest keyframe interval (1-5 s) that evenly divides every chunk boundary.

    The interval divides the chunk duration, the overlap, and therefore the chunk step, so each
    overlapping chunk starts and ends on a keyframe and can be cut with ``-c copy`` cleanly.
    """
    common = math.gcd(max(1, int(chunk_duration_sec)), max(0, int(overlap_sec)))
    for candidate in range(MAX_KEYFRAME_INTERVAL_SEC, MIN_KEYFRAME_INTERVAL_SEC - 1, -1):
        if common % candidate == 0:
            return candidate
    return MIN_KEYFRAME_INTERVAL_SEC


class _RawFrameStream:
    """Context manager reading fixed-size raw frames from an ffmpeg stdout pipe.

    A watchdog thread terminates the process on cancellation or timeout so blocking reads
    return promptly. stderr is drained concurrently so ffmpeg can never block on a full pipe.
    """

    def __init__(
        self,
        cmd: list[str],
        frame_bytes: int,
        stage_name: str,
        timeout_sec: float,
        cancel_event: threading.Event | None,
    ) -> None:
        self._cmd = cmd
        self._frame_bytes = frame_bytes
        self._stage_name = stage_name
        self._timeout_sec = timeout_sec
        self._cancel_event = cancel_event
        self._proc: subprocess.Popen[bytes] | None = None
        self._stop = threading.Event()
        self._abort_reason: str | None = None
        self._stderr_tail = ""
        self._threads: list[threading.Thread] = []
        # One buffer reused for every frame: avoids allocating (and joining) 25 MB per 4K frame.
        self._buffer = bytearray(frame_bytes)

    def __enter__(self) -> _RawFrameStream:
        if self._cancel_event is not None and self._cancel_event.is_set():
            raise FFmpegCancelledError(f"{self._stage_name} cancelled before subprocess start.")
        try:
            self._proc = subprocess.Popen(
                self._cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )
        except FileNotFoundError as exc:
            raise RuntimeError(
                f"Required binary '{self._cmd[0]}' for {self._stage_name} was not found on PATH."
            ) from exc
        deadline = time.monotonic() + self._timeout_sec
        for target, args in (
            (self._drain_stderr, ()),
            (self._watch, (deadline,)),
        ):
            thread = threading.Thread(target=target, args=args, daemon=True)
            thread.start()
            self._threads.append(thread)
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> None:
        self._stop.set()
        proc = self._proc
        if proc is not None:
            FFmpegClient._terminate_process(proc)  # type: ignore[arg-type]
            for stream in (proc.stdout, proc.stderr):
                self._close_quietly(stream)
        for thread in self._threads:
            thread.join(timeout=2.0)

    def read_frame(self) -> bytearray | None:
        """Read exactly one frame into the shared buffer and return it.

        Returns None at end of stream (a partial trailing frame is dropped). The returned buffer
        is overwritten by the next call, so callers must copy or consume it immediately.
        """
        assert self._proc is not None and self._proc.stdout is not None
        view = memoryview(self._buffer)
        filled = 0
        while filled < self._frame_bytes:
            read = self._proc.stdout.readinto(view[filled:])
            if not read:
                return None
            filled += read
        return self._buffer

    def finish(self) -> None:
        """Validate how the process ended; raise for cancellation, timeout, or non-zero exit."""
        assert self._proc is not None
        try:
            self._proc.wait(timeout=_STREAM_EXIT_TIMEOUT_SEC)
        except subprocess.TimeoutExpired:
            FFmpegClient._terminate_process(self._proc)  # type: ignore[arg-type]
        self._stop.set()
        for thread in self._threads:
            thread.join(timeout=2.0)
        if self._abort_reason == "cancelled":
            raise FFmpegCancelledError(f"{self._stage_name} cancelled while running.")
        if self._abort_reason == "timeout":
            raise TimeoutError(f"{self._stage_name} timed out after {self._timeout_sec:.1f}s.")
        if self._proc.returncode != 0:
            raise RuntimeError(
                f"{self._stage_name} failed (exit {self._proc.returncode}): {self._stderr_tail}"
            )

    def _drain_stderr(self) -> None:
        assert self._proc is not None
        try:
            raw = self._proc.stderr.read() if self._proc.stderr is not None else b""
        except (OSError, ValueError):
            return
        self._stderr_tail = raw.decode("utf-8", errors="replace").strip()[-_STDERR_TAIL_CHARS:]

    def _watch(self, deadline: float) -> None:
        assert self._proc is not None
        while not self._stop.wait(_STREAM_POLL_INTERVAL_SEC):
            if self._cancel_event is not None and self._cancel_event.is_set():
                self._abort_reason = "cancelled"
            elif time.monotonic() > deadline:
                self._abort_reason = "timeout"
            else:
                continue
            FFmpegClient._terminate_process(self._proc)  # type: ignore[arg-type]
            return

    @staticmethod
    def _close_quietly(stream: BinaryIO | None) -> None:
        if stream is None:
            return
        try:
            stream.close()
        except (OSError, ValueError):
            pass


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
        http_client: httpx.AsyncClient | None = None,
        hwaccel: str = "auto",
        video_encoder: str = "auto",
        extraction_threads: int = 0,
        extraction_hwaccel: str | None = None,
    ) -> None:
        if ffprobe_timeout_sec <= 0:
            raise ValueError("ffprobe_timeout_sec must be positive.")
        if ffmpeg_timeout_sec <= 0:
            raise ValueError("ffmpeg_timeout_sec must be positive.")
        if download_read_timeout_sec <= 0:
            raise ValueError("download_read_timeout_sec must be positive.")
        if download_total_timeout_sec <= 0:
            raise ValueError("download_total_timeout_sec must be positive.")
        if http_client is not None and not isinstance(http_client, httpx.AsyncClient):
            raise TypeError(
                "http_client must be an httpx.AsyncClient or None; "
                "synchronous clients cannot interrupt slow response headers."
            )
        self._ffmpeg_bin = ffmpeg_bin
        self._ffprobe_bin = ffprobe_bin
        self._ffprobe_timeout_sec = ffprobe_timeout_sec
        self._ffmpeg_timeout_sec = ffmpeg_timeout_sec
        self._download_read_timeout_sec = download_read_timeout_sec
        self._download_total_timeout_sec = download_total_timeout_sec
        self._http_client = http_client
        self._hwaccel = (hwaccel or "auto").strip().lower()
        self._video_encoder_setting = (video_encoder or "auto").strip().lower()
        # 0 keeps ffmpeg's default decoder threading; a small cap shrinks the 4K decoder's RAM.
        self._extraction_threads = max(0, int(extraction_threads))
        # Pass 2 decoders default to the Pass 1 setting; hardware surface pools can multiply the
        # per-decoder RAM of 4K HEVC, so deployments usually set this to "none".
        self._extraction_hwaccel = (
            self._hwaccel
            if extraction_hwaccel is None
            else (extraction_hwaccel.strip().lower() or "auto")
        )
        self._encoder_candidates: list[str] | None = None
        self._encoder_lock = threading.Lock()

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

        try:
            self._run_coro(
                self._download_source_video_async(
                    uri,
                    dest_path,
                    timeout,
                    deadline,
                    cancel_event,
                )
            )
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
            raise RuntimeError(f"Failed to download source video ({type(exc).__name__}).") from None
        except BaseException:
            dest_path.unlink(missing_ok=True)
            raise
        return dest_path

    async def _download_source_video_async(
        self,
        uri: str,
        dest_path: Path,
        timeout: httpx.Timeout,
        deadline: float,
        cancel_event: threading.Event | None,
        poll_interval_sec: float = 0.05,
    ) -> None:
        async def _stream_download() -> None:
            if isinstance(self._http_client, httpx.AsyncClient):
                async with self._http_client.stream(
                    "GET",
                    uri,
                    follow_redirects=True,
                    timeout=timeout,
                ) as response:
                    await self._consume_async_response(response, dest_path, deadline, cancel_event)
                return

            async with (
                httpx.AsyncClient(follow_redirects=True, timeout=timeout) as client,
                client.stream("GET", uri) as response,
            ):
                await self._consume_async_response(response, dest_path, deadline, cancel_event)

        download_task = asyncio.create_task(_stream_download())
        try:
            while not download_task.done():
                if cancel_event is not None and cancel_event.is_set():
                    download_task.cancel()
                    await asyncio.gather(download_task, return_exceptions=True)
                    raise FFmpegCancelledError("Source video download cancelled.")
                remaining = deadline - time.monotonic()
                if remaining <= 0.0:
                    download_task.cancel()
                    await asyncio.gather(download_task, return_exceptions=True)
                    raise TimeoutError(
                        "Source video download exceeded total timeout "
                        f"of {self._download_total_timeout_sec:.1f}s."
                    )
                wait_slice = (
                    min(remaining, poll_interval_sec) if cancel_event is not None else remaining
                )
                done, _ = await asyncio.wait({download_task}, timeout=wait_slice)
                if done:
                    break
            await download_task
        except BaseException:
            if not download_task.done():
                download_task.cancel()
                await asyncio.gather(download_task, return_exceptions=True)
            raise

    async def _consume_async_response(
        self,
        response: httpx.Response,
        dest_path: Path,
        deadline: float,
        cancel_event: threading.Event | None,
    ) -> None:
        self._check_download_cancelled(cancel_event)
        response.raise_for_status()
        with dest_path.open("wb") as handle:
            async for chunk in response.aiter_bytes():
                self._check_download_cancelled(cancel_event)
                if time.monotonic() > deadline:
                    raise TimeoutError(
                        "Source video download exceeded total timeout "
                        f"of {self._download_total_timeout_sec:.1f}s."
                    )
                handle.write(chunk)
                self._check_download_cancelled(cancel_event)
        self._check_download_cancelled(cancel_event)

    @staticmethod
    def _run_coro(coro: Coroutine[Any, Any, None]) -> None:
        try:
            asyncio.get_running_loop()
        except RuntimeError:
            asyncio.run(coro)
            return
        with ThreadPoolExecutor(max_workers=1) as pool:
            pool.submit(asyncio.run, coro).result()

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
        timings: dict[str, float] | None = None,
    ) -> list[VideoChunk]:
        """Build overlapping Pass 1 chunks from a single one-pass 720p mezzanine transcode.

        The source (often 4K) is decoded exactly once into a keyframe-aligned mezzanine; each
        chunk is then cut with ``-c copy``, so no chunk re-decodes the 4K source.

        Args:
            timings: Optional dict that receives ``pass1Mezzanine`` and ``pass1ChunkSlicing``
                elapsed seconds.
        """
        if target_height < 2:
            raise ValueError("target_height must be at least 2 pixels for yuv420p encoding.")
        output_dir.mkdir(parents=True, exist_ok=True)
        keyframe_interval = compute_keyframe_interval(chunk_duration_sec, overlap_sec)
        windows = self.compute_chunk_windows(
            total_duration_sec=probe.duration_sec,
            chunk_duration_sec=chunk_duration_sec,
            overlap_sec=overlap_sec,
            align_sec=float(keyframe_interval),
        )

        t_mezzanine = time.monotonic()
        mezzanine_path = self.create_mezzanine(
            video_path=video_path,
            probe=probe,
            output_path=output_dir / "pass1_mezzanine.mp4",
            target_height=target_height,
            fps=fps,
            keyframe_interval_sec=keyframe_interval,
            cancel_event=cancel_event,
        )
        t_slicing = time.monotonic()
        chunks = self.slice_chunks(mezzanine_path, windows, output_dir, cancel_event=cancel_event)
        if timings is not None:
            timings["pass1Mezzanine"] = round(t_slicing - t_mezzanine, 3)
            timings["pass1ChunkSlicing"] = round(time.monotonic() - t_slicing, 3)
        return chunks

    def create_mezzanine(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        output_path: Path,
        target_height: int,
        fps: float,
        keyframe_interval_sec: int,
        cancel_event: threading.Event | None = None,
    ) -> Path:
        """Transcode the source once into a downscaled H.264 mezzanine with regular keyframes.

        Uses hardware decode (``-hwaccel``) and walks the encoder priority list
        (``h264_nvenc`` -> ``h264_qsv`` -> ``h264_amf`` -> ``libx264``): an encoder that fails at
        runtime is dropped (for the life of this client) and the next candidate is tried, with
        ``libx264`` as the final fallback.

        ``fps`` is applied before ``scale`` so a 30/60 fps 4K source is decimated to the
        mezzanine rate first; scaling every 4K frame only to discard most of them is ~4x slower.
        """
        last_error: RuntimeError | None = None
        for encoder in self._video_encoder_candidates(cancel_event):
            cmd = self._build_mezzanine_command(
                video_path, probe, output_path, encoder, target_height, fps, keyframe_interval_sec
            )
            try:
                self._run_mezzanine(cmd, cancel_event)
            except FFmpegCancelledError:
                raise
            except RuntimeError as exc:
                last_error = exc
                if encoder == LIBX264_ENCODER:
                    break
                logger.warning("Video encoder '%s' failed (%s); trying the next.", encoder, exc)
                self._discard_video_encoder(encoder)
                continue
            return output_path
        assert last_error is not None
        raise last_error

    def slice_chunks(
        self,
        mezzanine_path: Path,
        windows: list[tuple[float, float]],
        output_dir: Path,
        cancel_event: threading.Event | None = None,
    ) -> list[VideoChunk]:
        """Cut overlapping chunks from the mezzanine with ``-c copy`` (no re-encode)."""
        output_dir.mkdir(parents=True, exist_ok=True)
        chunks: list[VideoChunk] = []
        for idx, (start_sec, end_sec) in enumerate(windows):
            chunk_path = output_dir / f"pass1_chunk_{idx:04d}.mp4"
            cmd = [
                self._ffmpeg_bin,
                "-y",
                "-hide_banner",
                "-ss",
                f"{start_sec:.3f}",
                "-i",
                str(mezzanine_path),
                "-t",
                f"{max(0.5, end_sec - start_sec):.3f}",
                "-c",
                "copy",
                "-avoid_negative_ts",
                "make_zero",
                "-movflags",
                "+faststart",
                str(chunk_path),
            ]
            self._run_command(
                cmd,
                "ffmpeg pass1 chunk slicing",
                timeout_sec=self._ffmpeg_timeout_sec,
                cancel_event=cancel_event,
            )
            chunks.append(
                VideoChunk(index=idx, start_sec=start_sec, end_sec=end_sec, path=chunk_path)
            )
        return chunks

    def _run_mezzanine(self, cmd: list[str], cancel_event: threading.Event | None) -> None:
        self._run_command(
            cmd,
            "ffmpeg pass1 mezzanine",
            timeout_sec=self._ffmpeg_timeout_sec,
            cancel_event=cancel_event,
        )

    def _build_mezzanine_command(
        self,
        video_path: Path,
        probe: VideoProbeResult,
        output_path: Path,
        encoder: str,
        target_height: int,
        fps: float,
        keyframe_interval_sec: int,
    ) -> list[str]:
        # yuv420p requires even width and height; keep valid even heights unchanged.
        effective_height = target_height - (target_height % 2)
        gop_frames = max(1, round(keyframe_interval_sec * fps))
        cmd = [self._ffmpeg_bin, "-y", "-hide_banner", *self._hwaccel_args()]
        cmd += ["-i", str(video_path), "-vf", f"fps={fps},scale=-2:{effective_height}"]
        cmd += ["-c:v", encoder, *_ENCODER_QUALITY_ARGS.get(encoder, []), "-pix_fmt", "yuv420p"]
        # No B-frames: at the low mezzanine fps a B-frame reorder delay (e.g. 2 x 0.5 s) would
        # make `-c copy` chunks start late and drift against the audio track.
        cmd += ["-bf", "0", "-g", str(gop_frames)]
        cmd += ["-force_key_frames", f"expr:gte(t,n_forced*{keyframe_interval_sec})"]
        if probe.has_audio:
            cmd += ["-c:a", "aac", "-ac", "1", "-ar", "16000", "-b:a", "32k"]
        else:
            cmd.append("-an")
        cmd += ["-movflags", "+faststart", str(output_path)]
        return cmd

    def _hwaccel_args(self, hwaccel: str | None = None) -> list[str]:
        selected = self._hwaccel if hwaccel is None else hwaccel
        if selected in ("", HWACCEL_DISABLED):
            return []
        return ["-hwaccel", selected]

    def _video_encoder_candidates(self, cancel_event: threading.Event | None) -> list[str]:
        """Return the ordered encoders to try (always ending with ``libx264``)."""
        with self._encoder_lock:
            if self._encoder_candidates is None:
                self._encoder_candidates = self._detect_video_encoders(cancel_event)
            return list(self._encoder_candidates)

    def _discard_video_encoder(self, encoder: str) -> None:
        with self._encoder_lock:
            if self._encoder_candidates is not None and encoder in self._encoder_candidates:
                self._encoder_candidates.remove(encoder)

    def _detect_video_encoders(self, cancel_event: threading.Event | None) -> list[str]:
        if self._video_encoder_setting != "auto":
            explicit = [self._video_encoder_setting]
            return explicit if explicit == [LIBX264_ENCODER] else [*explicit, LIBX264_ENCODER]
        try:
            completed = self._run_command(
                [self._ffmpeg_bin, "-hide_banner", "-encoders"],
                "ffmpeg encoder probe",
                timeout_sec=_ENCODER_PROBE_TIMEOUT_SEC,
                cancel_event=cancel_event,
            )
        except FFmpegCancelledError:
            raise
        except (RuntimeError, TimeoutError) as exc:
            logger.warning("Encoder auto-detection failed (%s); using %s.", exc, LIBX264_ENCODER)
            return [LIBX264_ENCODER]
        available = {
            match.group(1)
            for line in (completed.stdout or "").splitlines()
            if (match := _ENCODER_LINE_PATTERN.match(line))
        }
        candidates = [name for name in ENCODER_PRIORITY if name in available]
        if LIBX264_ENCODER not in candidates:
            candidates.append(LIBX264_ENCODER)
        logger.info("H.264 encoder candidates for the Pass 1 mezzanine: %s.", candidates)
        return candidates

    def extract_window_frames(
        self,
        video_path: Path,
        center_sec: float,
        half_window_sec: float,
        total_duration_sec: float,
        sample_fps: float,
        frame_width: int,
        frame_height: int,
        detector_input_width: int | None = None,
        cancel_event: threading.Event | None = None,
    ) -> list[ExtractedFrame]:
        """Stream frames in a narrow [t - d, t + d] window through an ffmpeg ``rawvideo`` pipe.

        Nothing is written to disk. Each native frame is downscaled to ``detector_input_width``
        for detection and kept only as a compact JPEG copy for later cap cropping, so peak RAM
        per window stays far below holding every raw 4K frame.

        Args:
            frame_width: Native source width (from ffprobe), needed to slice the raw stream.
            frame_height: Native source height (from ffprobe).
            detector_input_width: Detection image width; ``None`` keeps native resolution.
        """
        if frame_width < 2 or frame_height < 2:
            raise ValueError("frame_width and frame_height must be at least 2 pixels.")
        win_start = max(0.0, center_sec - half_window_sec)
        win_end = min(total_duration_sec, center_sec + half_window_sec)
        win_dur = max(0.25, win_end - win_start)
        cmd = [self._ffmpeg_bin, "-hide_banner", "-loglevel", "error", "-nostdin"]
        cmd += self._hwaccel_args(self._extraction_hwaccel)
        if self._extraction_threads > 0:
            cmd += ["-threads", str(self._extraction_threads)]
        cmd += ["-ss", f"{win_start:.3f}", "-i", str(video_path), "-t", f"{win_dur:.3f}"]
        cmd += ["-an", "-sn", "-vf", f"fps={sample_fps}"]
        cmd += ["-f", "rawvideo", "-pix_fmt", "bgr24", "pipe:1"]

        extracted: list[ExtractedFrame] = []
        with _RawFrameStream(
            cmd,
            frame_bytes=frame_width * frame_height * 3,
            stage_name="ffmpeg pass2 window extraction",
            timeout_sec=self._ffmpeg_timeout_sec,
            cancel_event=cancel_event,
        ) as stream:
            while (raw := stream.read_frame()) is not None:
                idx = len(extracted)
                native = np.frombuffer(raw, dtype=np.uint8).reshape(frame_height, frame_width, 3)
                video_time = min(total_duration_sec, win_start + idx / sample_fps)
                extracted.append(
                    self._to_extracted_frame(idx, video_time, native, detector_input_width)
                )
            stream.finish()
        return extracted

    @staticmethod
    def _to_extracted_frame(
        index: int,
        video_time_sec: float,
        native_bgr: np.ndarray,
        detector_input_width: int | None,
    ) -> ExtractedFrame:
        height, width = native_bgr.shape[:2]
        if detector_input_width is None or width <= detector_input_width:
            return ExtractedFrame(
                index=index, video_time_sec=video_time_sec, image_bgr=native_bgr.copy()
            )
        detection_height = max(2, round(height * detector_input_width / width))
        detection_image = cv2.resize(
            native_bgr,
            (detector_input_width, detection_height),
            interpolation=cv2.INTER_AREA,
        )
        ok, encoded = cv2.imencode(
            ".jpg", native_bgr, [cv2.IMWRITE_JPEG_QUALITY, NATIVE_JPEG_QUALITY]
        )
        if not ok:
            raise RuntimeError(f"Failed to compress native copy of Pass 2 frame {index}.")
        return ExtractedFrame(
            index=index,
            video_time_sec=video_time_sec,
            image_bgr=detection_image,
            native_width=width,
            native_height=height,
            native_jpeg=encoded.tobytes(),
        )

    @staticmethod
    def compute_chunk_windows(
        total_duration_sec: float,
        chunk_duration_sec: int,
        overlap_sec: int,
        align_sec: float = 0.0,
    ) -> list[tuple[float, float]]:
        """Compute deterministic overlapping [start, end] time ranges covering the full video.

        A trailing window shorter than half a chunk is backed up so it spans a full chunk (start
        clamped to >= 0). Windows fully contained in the backed-up tail are dropped, so no
        duplicate windows are emitted. A clip no longer than one chunk yields one window.

        Args:
            align_sec: When positive, the backed-up tail start is floored to a multiple of this
                value (the keyframe interval) so ``-c copy`` slicing lands on a keyframe.
        """
        if total_duration_sec <= 0:
            return []
        if total_duration_sec <= chunk_duration_sec:
            return [(0.0, round(total_duration_sec, 3))]

        step = max(1, chunk_duration_sec - overlap_sec)
        windows: list[tuple[float, float]] = []
        cursor = 0.0
        while True:
            end_sec = min(total_duration_sec, cursor + chunk_duration_sec)
            windows.append((cursor, end_sec))
            if end_sec >= total_duration_sec:
                break
            cursor += step

        windows = FFmpegClient._back_up_short_tail(
            windows, total_duration_sec, chunk_duration_sec, align_sec
        )
        unique: list[tuple[float, float]] = []
        for start_sec, end_sec in windows:
            rounded = (round(start_sec, 3), round(end_sec, 3))
            if rounded not in unique:
                unique.append(rounded)
        return unique

    @staticmethod
    def _back_up_short_tail(
        windows: list[tuple[float, float]],
        total_duration_sec: float,
        chunk_duration_sec: int,
        align_sec: float,
    ) -> list[tuple[float, float]]:
        tail_start, tail_end = windows[-1]
        if tail_end - tail_start >= MIN_TAIL_FRACTION * chunk_duration_sec:
            return windows
        new_start = max(0.0, total_duration_sec - chunk_duration_sec)
        if align_sec > 0.0:
            new_start = math.floor(new_start / align_sec + 1e-9) * align_sec
        kept = [window for window in windows[:-1] if window[0] < new_start]
        return [*kept, (new_start, tail_end)]

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
            raise RuntimeError(f"{stage_name} failed (exit {proc.returncode}): {stderr_tail}")
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
