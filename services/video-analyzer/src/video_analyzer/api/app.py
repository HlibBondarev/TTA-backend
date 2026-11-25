"""FastAPI application exposing internal endpoints for .NET backend orchestration."""

from __future__ import annotations

import hmac
from contextlib import asynccontextmanager
from typing import Any
from uuid import UUID

from fastapi import Depends, FastAPI, Header, HTTPException, Response, status
from fastapi.responses import JSONResponse

from video_analyzer import __version__
from video_analyzer.api.job_store import JobSnapshot, JobStore
from video_analyzer.config import Settings
from video_analyzer.logging_utils import configure_safe_http_logging
from video_analyzer.pipeline.factory import build_default_orchestrator
from video_analyzer.pipeline.orchestrator import VideoAnalysisOrchestrator
from video_analyzer.schemas.request import AnalyzeJobRequest


def create_app(
    settings: Settings | None = None,
    orchestrator: VideoAnalysisOrchestrator | None = None,
) -> FastAPI:
    """Create the FastAPI application with internal API key security."""
    configure_safe_http_logging()
    active_settings = settings or Settings()
    internal_api_key = active_settings.require_internal_api_key()
    active_orchestrator = orchestrator or build_default_orchestrator(active_settings)
    job_store = JobStore(
        orchestrator=active_orchestrator,
        callback_allowed_origins=active_settings.resolved_callback_allowed_origins,
        callback_api_key=active_settings.callback_api_key,
    )

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        try:
            yield
        finally:
            job_store.shutdown()

    application = FastAPI(
        title="TTA Video Analyzer Worker",
        version=__version__,
        description=(
            "Internal two-pass Water Polo video analysis service orchestrated by TTA-backend."
        ),
        lifespan=lifespan,
    )
    application.state.settings = active_settings
    application.state.job_store = job_store

    def verify_internal_api_key(
        x_internal_api_key: str | None = Header(default=None, alias="X-Internal-Api-Key"),
    ) -> None:
        if not x_internal_api_key or not hmac.compare_digest(x_internal_api_key, internal_api_key):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Invalid or missing X-Internal-Api-Key.",
            )

    @application.get("/health")
    def health() -> dict[str, Any]:
        return {
            "status": "ok",
            "version": __version__,
            "detectorProvider": active_settings.detector_provider,
            "detectorModel": active_settings.detector_model_path_or_id,
            "capResolverProvider": active_settings.cap_resolver_provider,
            "geminiConfigured": bool(active_settings.gemini_api_key.strip()),
            "roboflowConfigured": bool(active_settings.roboflow_api_key.strip()),
        }

    @application.post(
        "/v1/analysis/jobs",
        status_code=status.HTTP_202_ACCEPTED,
        dependencies=[Depends(verify_internal_api_key)],
    )
    def submit_job(request: AnalyzeJobRequest) -> dict[str, Any]:
        try:
            snapshot = job_store.submit_and_snapshot(request)
        except ValueError as exc:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=str(exc),
            ) from exc
        return {
            "jobId": str(snapshot.job_id),
            "executionId": str(snapshot.execution_id),
            "matchId": str(snapshot.match_id),
            "status": snapshot.status,
            "stage": snapshot.stage,
            "progressPercent": snapshot.progress_percent,
        }

    @application.get(
        "/v1/analysis/jobs/{job_id}",
        dependencies=[Depends(verify_internal_api_key)],
    )
    def get_job(job_id: UUID) -> JSONResponse:
        snapshot = job_store.get_snapshot(job_id)
        if snapshot is None:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=f"Video analysis job '{job_id}' was not found.",
            )
        return JSONResponse(content=_serialize_job(snapshot))

    @application.delete(
        "/v1/analysis/jobs/{job_id}",
        status_code=status.HTTP_204_NO_CONTENT,
        dependencies=[Depends(verify_internal_api_key)],
    )
    def cancel_job(job_id: UUID) -> Response:
        if not job_store.cancel(job_id):
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=f"Video analysis job '{job_id}' was not found.",
            )
        return Response(status_code=status.HTTP_204_NO_CONTENT)

    return application


def _serialize_job(snapshot: JobSnapshot) -> dict[str, Any]:
    return {
        "jobId": str(snapshot.job_id),
        "executionId": str(snapshot.execution_id),
        "matchId": str(snapshot.match_id),
        "status": snapshot.status,
        "stage": snapshot.stage,
        "progressPercent": snapshot.progress_percent,
        "errorMessage": snapshot.error_message,
        "createdAt": snapshot.created_at.isoformat(),
        "updatedAt": snapshot.updated_at.isoformat(),
        "draft": snapshot.draft.model_dump(mode="json", by_alias=True) if snapshot.draft else None,
    }


def __getattr__(name: str) -> Any:
    if name == "app":
        return create_app()
    raise AttributeError(f"module {__name__!r} has no attribute {name!r}")
