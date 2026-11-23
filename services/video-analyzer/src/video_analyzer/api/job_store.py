"""Thread-safe in-memory job manager for asynchronous analysis execution and callbacks."""

from __future__ import annotations

import logging
import threading
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Literal
from uuid import UUID

import httpx

from video_analyzer.pipeline.orchestrator import JobCancelledError, VideoAnalysisOrchestrator
from video_analyzer.schemas.draft import VideoAnalysisDraft
from video_analyzer.schemas.request import AnalyzeJobRequest

logger = logging.getLogger(__name__)

JobStatus = Literal["queued", "processing", "completed", "failed", "cancelled"]


@dataclass(slots=True)
class JobRecord:
    """Mutable state for a single queued/running/completed analysis job."""

    job_id: UUID
    match_id: UUID
    status: JobStatus = "queued"
    stage: str = "queued"
    progress_percent: int = 0
    error_message: str | None = None
    draft: VideoAnalysisDraft | None = None
    created_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    updated_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    cancel_event: threading.Event = field(default_factory=threading.Event)


class JobStore:
    """Executes video analysis jobs in background worker threads and posts optional callbacks."""

    def __init__(
        self,
        orchestrator: VideoAnalysisOrchestrator,
        internal_api_key: str,
        max_workers: int = 2,
    ) -> None:
        self._orchestrator = orchestrator
        self._internal_api_key = internal_api_key
        self._executor = ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="va-job")
        self._lock = threading.Lock()
        self._jobs: dict[UUID, JobRecord] = {}

    def submit(self, request: AnalyzeJobRequest) -> JobRecord:
        """Enqueue a new job and immediately return its queued state."""
        with self._lock:
            existing = self._jobs.get(request.job_id)
            if existing is not None and existing.status in ("queued", "processing"):
                return existing
            record = JobRecord(job_id=request.job_id, match_id=request.match_id)
            self._jobs[request.job_id] = record

        self._executor.submit(self._run_job, request, record)
        return record

    def get(self, job_id: UUID) -> JobRecord | None:
        with self._lock:
            return self._jobs.get(job_id)

    def cancel(self, job_id: UUID) -> bool:
        with self._lock:
            record = self._jobs.get(job_id)
            if record is None:
                return False
            record.cancel_event.set()
            if record.status in ("queued", "processing"):
                record.status = "cancelled"
                record.stage = "cancelled"
                record.updated_at = datetime.now(UTC)
            return True

    def shutdown(self) -> None:
        self._executor.shutdown(wait=False, cancel_futures=True)

    def _run_job(self, request: AnalyzeJobRequest, record: JobRecord) -> None:
        if record.cancel_event.is_set():
            return

        self._update(record, status="processing", stage="starting", progress=1)

        def on_progress(stage: str, pct: int) -> None:
            self._update(record, stage=stage, progress=pct)

        try:
            draft = self._orchestrator.run(
                request=request,
                cancel_event=record.cancel_event,
                progress_callback=on_progress,
            )
            if record.cancel_event.is_set():
                self._update(record, status="cancelled", stage="cancelled")
                return
            self._update(
                record,
                status="completed",
                stage="completed",
                progress=100,
                draft=draft,
            )
            self._dispatch_callback(request.callback_url, record)
        except JobCancelledError:
            self._update(record, status="cancelled", stage="cancelled")
            self._dispatch_callback(request.callback_url, record)
        except Exception as exc:
            logger.exception("Analysis job %s failed.", request.job_id)
            self._update(
                record,
                status="failed",
                stage="failed",
                error_message=str(exc),
            )
            self._dispatch_callback(request.callback_url, record)

    def _update(
        self,
        record: JobRecord,
        status: JobStatus | None = None,
        stage: str | None = None,
        progress: int | None = None,
        draft: VideoAnalysisDraft | None = None,
        error_message: str | None = None,
    ) -> None:
        with self._lock:
            if record.status == "cancelled" and status not in (None, "cancelled"):
                return
            if status is not None:
                record.status = status
            if stage is not None:
                record.stage = stage
            if progress is not None:
                record.progress_percent = progress
            if draft is not None:
                record.draft = draft
            if error_message is not None:
                record.error_message = error_message
            record.updated_at = datetime.now(UTC)

    def _dispatch_callback(self, callback_url: str | None, record: JobRecord) -> None:
        if not callback_url:
            return
        payload = {
            "jobId": str(record.job_id),
            "matchId": str(record.match_id),
            "status": record.status,
            "stage": record.stage,
            "progressPercent": record.progress_percent,
            "errorMessage": record.error_message,
            "draft": record.draft.model_dump(mode="json", by_alias=True) if record.draft else None,
        }
        try:
            with httpx.Client(timeout=30.0) as client:
                response = client.post(
                    callback_url,
                    json=payload,
                    headers={"X-Internal-Api-Key": self._internal_api_key},
                )
                response.raise_for_status()
        except Exception as exc:
            logger.warning(
                "Failed posting callback for job %s to %s: %s",
                record.job_id,
                callback_url,
                exc,
            )
