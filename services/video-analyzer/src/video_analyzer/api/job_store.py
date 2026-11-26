"""Thread-safe in-memory job manager for asynchronous analysis execution and callbacks."""

from __future__ import annotations

import logging
import threading
from collections.abc import Iterable
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any, Literal
from urllib.parse import urlsplit
from uuid import UUID, uuid4

import httpx

from video_analyzer.config import extract_origin, normalize_origin
from video_analyzer.pipeline.orchestrator import JobCancelledError, VideoAnalysisOrchestrator
from video_analyzer.schemas.draft import VideoAnalysisDraft
from video_analyzer.schemas.request import AnalyzeJobRequest

logger = logging.getLogger(__name__)

JobStatus = Literal["queued", "processing", "completed", "failed", "cancelled"]
TerminalJobStatus = Literal["completed", "failed", "cancelled"]
_TERMINAL_STATUSES: frozenset[str] = frozenset({"completed", "failed", "cancelled"})
_INVALID_CALLBACK_URL_PLACEHOLDER = "<invalid-callback-url>"


@dataclass(slots=True)
class JobRecord:
    """Mutable state for a single queued/running/completed analysis job."""

    job_id: UUID
    match_id: UUID
    execution_id: UUID = field(default_factory=uuid4)
    status: JobStatus = "queued"
    stage: str = "queued"
    progress_percent: int = 0
    error_message: str | None = None
    draft: VideoAnalysisDraft | None = None
    callback_url: str | None = None
    callback_dispatched: bool = False
    callback_in_flight: bool = False
    finalized: bool = False
    created_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    updated_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    cancel_event: threading.Event = field(default_factory=threading.Event)


@dataclass(frozen=True, slots=True)
class JobSnapshot:
    """Immutable point-in-time snapshot of a job record captured under lock."""

    job_id: UUID
    execution_id: UUID
    match_id: UUID
    status: JobStatus
    stage: str
    progress_percent: int
    error_message: str | None
    created_at: datetime
    updated_at: datetime
    draft: VideoAnalysisDraft | None


class JobStore:
    """Executes video analysis jobs in background worker threads and posts optional callbacks."""

    def __init__(
        self,
        orchestrator: VideoAnalysisOrchestrator,
        callback_allowed_origins: Iterable[str] = (),
        callback_api_key: str = "",
        max_workers: int = 2,
        max_retained_terminal_jobs: int = 500,
        terminal_job_ttl_sec: float = 3600.0,
        http_client: httpx.Client | None = None,
    ) -> None:
        if max_retained_terminal_jobs < 0:
            raise ValueError("max_retained_terminal_jobs must be non-negative.")
        if terminal_job_ttl_sec < 0.0:
            raise ValueError("terminal_job_ttl_sec must be non-negative.")

        self._orchestrator = orchestrator
        self._allowed_callback_origins = frozenset(
            normalize_origin(origin) for origin in callback_allowed_origins if origin.strip()
        )
        self._callback_api_key = callback_api_key.strip()
        self._max_retained_terminal_jobs = max_retained_terminal_jobs
        self._terminal_job_ttl_sec = terminal_job_ttl_sec
        self._http_client = http_client
        self._executor = ThreadPoolExecutor(max_workers=max_workers, thread_name_prefix="va-job")
        self._lock = threading.Lock()
        self._condition = threading.Condition(self._lock)
        self._shutdown = False
        self._jobs: dict[UUID, JobRecord] = {}

    def validate_callback_url(self, callback_url: str | None) -> str | None:
        """Validate a callback URL against the configured trusted origin allowlist."""
        if callback_url is None:
            return None
        stripped = callback_url.strip()
        if not stripped:
            raise ValueError("callbackUrl must not be blank when provided.")

        origin = extract_origin(stripped)
        if not self._allowed_callback_origins:
            raise ValueError(
                "callbackUrl cannot be used because CALLBACK_ALLOWED_ORIGINS is not configured."
            )
        if origin not in self._allowed_callback_origins:
            raise ValueError(f"callbackUrl origin '{origin}' is not in CALLBACK_ALLOWED_ORIGINS.")
        return stripped

    def submit(self, request: AnalyzeJobRequest) -> JobRecord:
        """Validate callback target, enqueue a job, and immediately return its queued state."""
        record, _ = self._submit_internal(request)
        return record

    def submit_and_snapshot(self, request: AnalyzeJobRequest) -> JobSnapshot:
        """Validate callback target, enqueue a job, and return an atomic initial snapshot."""
        _, snapshot = self._submit_internal(request)
        return snapshot

    def _submit_internal(self, request: AnalyzeJobRequest) -> tuple[JobRecord, JobSnapshot]:
        validated_callback_url = self.validate_callback_url(request.callback_url)

        with self._condition:
            while not self._shutdown:
                existing = self._jobs.get(request.job_id)
                if existing is not None and existing.callback_in_flight:
                    self._condition.wait()
                    continue
                break
            if self._shutdown:
                raise RuntimeError("JobStore has been shut down.")

            now = datetime.now(UTC)
            self._prune_terminal_jobs_locked(now)
            existing = self._jobs.get(request.job_id)
            if existing is not None and existing.status in ("queued", "processing"):
                return existing, self._to_snapshot(existing)
            record = JobRecord(
                job_id=request.job_id,
                match_id=request.match_id,
                callback_url=validated_callback_url,
                created_at=now,
                updated_at=now,
            )
            self._jobs[request.job_id] = record
            snapshot = self._to_snapshot(record)

        self._executor.submit(self._run_job, request, record)
        return record, snapshot

    def get(self, job_id: UUID) -> JobRecord | None:
        """Return an active or retained job record, pruning expired terminal records first."""
        now = datetime.now(UTC)
        with self._condition:
            self._prune_terminal_jobs_locked(now)
            return self._jobs.get(job_id)

    def get_snapshot(self, job_id: UUID) -> JobSnapshot | None:
        """Return an atomic job snapshot under lock, pruning expired terminal records first."""
        now = datetime.now(UTC)
        with self._condition:
            self._prune_terminal_jobs_locked(now)
            record = self._jobs.get(job_id)
            return self._to_snapshot(record) if record is not None else None

    @staticmethod
    def _to_snapshot(record: JobRecord) -> JobSnapshot:
        return JobSnapshot(
            job_id=record.job_id,
            execution_id=record.execution_id,
            match_id=record.match_id,
            status=record.status,
            stage=record.stage,
            progress_percent=record.progress_percent,
            error_message=record.error_message,
            created_at=record.created_at,
            updated_at=record.updated_at,
            draft=record.draft,
        )

    def cancel(self, job_id: UUID) -> bool:
        """Request cooperative cancellation for a tracked job."""
        with self._condition:
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
        """Signal active jobs to cancel, stop accepting new jobs, and cancel queued futures."""
        now = datetime.now(UTC)
        with self._condition:
            self._shutdown = True
            for record in self._jobs.values():
                if record.status in ("queued", "processing"):
                    record.cancel_event.set()
                    record.status = "cancelled"
                    record.stage = "cancelled"
                    record.updated_at = now
            self._condition.notify_all()
        self._executor.shutdown(wait=False, cancel_futures=True)

    def _run_job(self, request: AnalyzeJobRequest, record: JobRecord) -> None:
        if record.cancel_event.is_set() or not self._update(
            record, status="processing", stage="starting", progress=1
        ):
            if record.cancel_event.is_set():
                callback_url, payload = self._finalize_and_prepare_callback(record, "cancelled")
                self._deliver_if_prepared(record, callback_url, payload)
            return

        callback_url, payload = self._execute_and_finalize(request, record)
        self._deliver_if_prepared(record, callback_url, payload)

    def _execute_and_finalize(
        self,
        request: AnalyzeJobRequest,
        record: JobRecord,
    ) -> tuple[str | None, dict[str, Any] | None]:
        """Run the orchestrator and finalize the record, returning any callback to deliver."""

        def on_progress(stage: str, pct: int) -> None:
            self._update(record, stage=stage, progress=pct)

        try:
            draft = self._orchestrator.run(
                request=request,
                cancel_event=record.cancel_event,
                progress_callback=on_progress,
            )
        except JobCancelledError:
            return self._finalize_and_prepare_callback(record, "cancelled")
        except httpx.HTTPError as exc:
            safe_error = _summarize_job_http_error(exc)
            logger.error("Analysis job %s failed: %s", request.job_id, safe_error)
            return self._finalize_job_outcome(record, "failed", error_message=safe_error)
        except Exception as exc:
            logger.exception("Analysis job %s failed.", request.job_id)
            return self._finalize_job_outcome(record, "failed", error_message=str(exc))
        return self._finalize_job_outcome(record, "completed", draft=draft)

    def _finalize_job_outcome(
        self,
        record: JobRecord,
        target_status: TerminalJobStatus,
        draft: VideoAnalysisDraft | None = None,
        error_message: str | None = None,
    ) -> tuple[str | None, dict[str, Any] | None]:
        """Finalize with ``target_status`` unless cancellation was requested in the meantime."""
        if record.cancel_event.is_set():
            return self._finalize_and_prepare_callback(record, "cancelled")
        return self._finalize_and_prepare_callback(
            record,
            target_status,
            draft=draft,
            error_message=error_message,
        )

    def _deliver_if_prepared(
        self,
        record: JobRecord,
        callback_url: str | None,
        payload: dict[str, Any] | None,
    ) -> None:
        if callback_url is not None and payload is not None:
            self._deliver_callback_and_finalize(record, callback_url, payload)

    def _update(
        self,
        record: JobRecord,
        status: JobStatus | None = None,
        stage: str | None = None,
        progress: int | None = None,
    ) -> bool:
        with self._condition:
            if self._jobs.get(record.job_id) is not record:
                return False
            if record.status in _TERMINAL_STATUSES or record.cancel_event.is_set():
                return False
            if status is not None:
                record.status = status
            if stage is not None:
                record.stage = stage
            if progress is not None:
                record.progress_percent = progress
            record.updated_at = datetime.now(UTC)
            return True

    def _finalize_and_prepare_callback(
        self,
        record: JobRecord,
        target_status: TerminalJobStatus,
        draft: VideoAnalysisDraft | None = None,
        error_message: str | None = None,
    ) -> tuple[str | None, dict[str, Any] | None]:
        now = datetime.now(UTC)
        with self._condition:
            if self._jobs.get(record.job_id) is not record:
                return None, None

            if record.cancel_event.is_set() or record.status == "cancelled":
                final_status: TerminalJobStatus = "cancelled"
                final_draft = None
                final_error = None
            else:
                final_status = target_status
                final_draft = draft if final_status == "completed" else None
                final_error = error_message if final_status == "failed" else None

            record.status = final_status
            record.stage = final_status
            if final_status == "completed":
                record.progress_percent = 100
            record.draft = final_draft
            record.error_message = final_error
            record.updated_at = now

            if not record.callback_url or record.callback_dispatched:
                record.finalized = True
                self._prune_terminal_jobs_locked(now, preserve_job_id=record.job_id)
                self._condition.notify_all()
                return None, None

            record.callback_dispatched = True
            record.callback_in_flight = True
            payload: dict[str, Any] = {
                "jobId": str(record.job_id),
                "executionId": str(record.execution_id),
                "matchId": str(record.match_id),
                "status": record.status,
                "stage": record.stage,
                "progressPercent": record.progress_percent,
                "errorMessage": record.error_message,
                "draft": (
                    record.draft.model_dump(mode="json", by_alias=True) if record.draft else None
                ),
            }
            return record.callback_url, payload

    def _deliver_callback_and_finalize(
        self,
        record: JobRecord,
        callback_url: str,
        payload: dict[str, Any],
    ) -> None:
        try:
            self._dispatch_callback(callback_url, payload, record.job_id)
        finally:
            now = datetime.now(UTC)
            with self._condition:
                record.callback_in_flight = False
                record.finalized = True
                record.updated_at = now
                if self._jobs.get(record.job_id) is record:
                    self._prune_terminal_jobs_locked(now, preserve_job_id=record.job_id)
                else:
                    self._prune_terminal_jobs_locked(now)
                self._condition.notify_all()

    def _prune_terminal_jobs_locked(
        self,
        now: datetime,
        preserve_job_id: UUID | None = None,
    ) -> None:
        terminal_records: list[tuple[UUID, JobRecord]] = [
            (jid, rec)
            for jid, rec in self._jobs.items()
            if rec.finalized and not rec.callback_in_flight and rec.status in _TERMINAL_STATUSES
        ]
        if not terminal_records:
            return

        retained: list[tuple[UUID, JobRecord]] = []
        for jid, rec in terminal_records:
            age_sec = (now - rec.updated_at).total_seconds()
            if jid != preserve_job_id and age_sec >= self._terminal_job_ttl_sec:
                del self._jobs[jid]
            else:
                retained.append((jid, rec))

        excess = len(retained) - self._max_retained_terminal_jobs
        if excess > 0:
            evictable = [(jid, rec) for jid, rec in retained if jid != preserve_job_id]
            evictable.sort(key=lambda item: item[1].updated_at)
            for jid, _ in evictable[:excess]:
                del self._jobs[jid]

    def _dispatch_callback(
        self,
        callback_url: str,
        payload: dict[str, Any],
        job_id: UUID,
    ) -> None:
        try:
            validated_url = self.validate_callback_url(callback_url)
            if not validated_url:
                return
            headers: dict[str, str] = {}
            if self._callback_api_key:
                headers["X-Callback-Api-Key"] = self._callback_api_key

            if self._http_client is not None:
                response = self._http_client.post(
                    validated_url,
                    json=payload,
                    headers=headers,
                    follow_redirects=False,
                )
                response.raise_for_status()
            else:
                with httpx.Client(timeout=30.0, follow_redirects=False) as client:
                    response = client.post(
                        validated_url,
                        json=payload,
                        headers=headers,
                    )
                    response.raise_for_status()
        except Exception as exc:
            logger.warning(
                "Failed posting callback for job %s to %s: %s",
                job_id,
                _sanitize_callback_url(callback_url),
                _summarize_callback_error(exc),
            )


def _sanitize_callback_url(callback_url: str) -> str:
    """Return scheme://host[:port]/path with userinfo, query, and fragment stripped."""
    stripped = callback_url.strip()
    if not stripped:
        return _INVALID_CALLBACK_URL_PLACEHOLDER
    try:
        parsed = urlsplit(stripped)
        scheme = parsed.scheme.lower()
        hostname = parsed.hostname
        port = parsed.port
    except ValueError:
        return _INVALID_CALLBACK_URL_PLACEHOLDER
    if not scheme or not hostname:
        return _INVALID_CALLBACK_URL_PLACEHOLDER
    formatted_host = f"[{hostname}]" if ":" in hostname else hostname
    port_part = f":{port}" if port is not None else ""
    return f"{scheme}://{formatted_host}{port_part}{parsed.path}"


def _summarize_callback_error(exc: Exception) -> str:
    """Return a concise error summary without leaking raw URLs or credentials."""
    if isinstance(exc, httpx.HTTPStatusError):
        return f"HTTPStatusError (HTTP {exc.response.status_code})"
    return type(exc).__name__


def _summarize_job_http_error(exc: httpx.HTTPError) -> str:
    """Summarize an unexpected HTTP failure without exposing signed URLs or credentials."""
    if isinstance(exc, httpx.HTTPStatusError):
        return f"Remote HTTP request failed (HTTP {exc.response.status_code})."
    return f"Remote HTTP request failed ({type(exc).__name__})."
