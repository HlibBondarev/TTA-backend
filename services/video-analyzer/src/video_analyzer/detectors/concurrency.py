"""Concurrency-limiting detector wrapper shared by every Pass 2 Phase A task."""

from __future__ import annotations

import threading
import time

import numpy as np

from video_analyzer.detectors.base import BaseObjectDetector, Detection


class ConcurrencyLimitedDetector(BaseObjectDetector):
    """Caps simultaneous ``detect`` calls on the wrapped detector with one global semaphore.

    The orchestrator creates a single instance per detector, so the limit applies across all
    Phase A tasks (not per event): ``PASS2_PHASE_A_MAX_CONCURRENCY x DETECTOR_MAX_CONCURRENCY``
    can never produce that many simultaneous remote uploads. A limit of ``1`` serializes calls,
    which is what local ONNX/YOLO providers need.
    """

    def __init__(self, inner: BaseObjectDetector, max_concurrency: int) -> None:
        if max_concurrency < 1:
            raise ValueError("max_concurrency must be at least 1.")
        self._inner = inner
        self._max_concurrency = max_concurrency
        self._semaphore = threading.BoundedSemaphore(max_concurrency)
        self.supports_concurrent_inference = max_concurrency > 1

    @property
    def max_concurrency(self) -> int:
        return self._max_concurrency

    @property
    def inner(self) -> BaseObjectDetector:
        return self._inner

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        with self._semaphore:
            return self._inner.detect(frame_bgr)

    def warmup(self) -> None:
        self._inner.warmup()

    def provider_info(self) -> dict[str, str]:
        return self._inner.provider_info()


class TimedDetector(BaseObjectDetector):
    """Accumulates wall-clock time spent inside ``detect`` (including queueing on the limiter).

    Create one instance per Pass 2 window; it is used by a single Phase A task at a time.
    """

    def __init__(self, inner: BaseObjectDetector) -> None:
        self._inner = inner
        self.elapsed_sec = 0.0

    def detect(self, frame_bgr: np.ndarray) -> list[Detection]:
        started = time.monotonic()
        try:
            return self._inner.detect(frame_bgr)
        finally:
            self.elapsed_sec += time.monotonic() - started

    def warmup(self) -> None:
        self._inner.warmup()

    def provider_info(self) -> dict[str, str]:
        return self._inner.provider_info()
