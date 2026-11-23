"""Security, CLI serve, callback trust, concurrency, and retention tests."""

from __future__ import annotations

import threading
import time
from datetime import timedelta
from pathlib import Path
from uuid import uuid4

import httpx
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import video_analyzer.api.app as app_module
from tests.test_orchestrator_and_api import _build_mock_orchestrator
from video_analyzer.api.app import create_app
from video_analyzer.api.job_store import JobStore
from video_analyzer.cli import main as cli_main
from video_analyzer.config import Settings
from video_analyzer.pipeline.orchestrator import JobCancelledError
from video_analyzer.schemas.request import AnalyzeJobRequest


def test_create_app_rejects_unset_or_placeholder_internal_api_key(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("INTERNAL_API_KEY", raising=False)
    monkeypatch.delenv("SERVICE_HOST", raising=False)
    orch = _build_mock_orchestrator()

    empty_settings = Settings(_env_file=None)
    assert empty_settings.service_host == "127.0.0.1"
    with pytest.raises(ValueError, match="INTERNAL_API_KEY"):
        create_app(settings=empty_settings, orchestrator=orch)

    placeholder_settings = Settings(_env_file=None, internal_api_key="change-me-local-dev-key")
    with pytest.raises(ValueError, match="INTERNAL_API_KEY"):
        create_app(settings=placeholder_settings, orchestrator=orch)

    monkeypatch.setenv("INTERNAL_API_KEY", "change-me-local-dev-key")
    with pytest.raises(ValueError, match="INTERNAL_API_KEY"):
        _ = app_module.app


def test_cli_serve_passes_loaded_settings_and_custom_env_file(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    custom_env = tmp_path / "custom.env"
    custom_env.write_text(
        "\n".join(
            [
                "INTERNAL_API_KEY=custom-cli-secret",
                "SERVICE_HOST=127.0.0.1",
                "SERVICE_PORT=8123",
                "CALLBACK_ALLOWED_ORIGINS=https://hooks.tta.local",
                "CALLBACK_API_KEY=outbound-hook-secret",
            ]
        ),
        encoding="utf-8",
    )

    captured: dict[str, object] = {}
    monkeypatch.setattr(
        "video_analyzer.cli.build_default_orchestrator",
        lambda settings: _build_mock_orchestrator(),
    )

    def fake_uvicorn_run(app: object, host: str, port: int) -> None:
        captured["app"] = app
        captured["host"] = host
        captured["port"] = port

    monkeypatch.setattr("video_analyzer.cli.uvicorn.run", fake_uvicorn_run)

    exit_code = cli_main(["serve", "--env-file", str(custom_env)])
    assert exit_code == 0
    served_app = captured["app"]
    assert isinstance(served_app, FastAPI)
    assert captured["host"] == "127.0.0.1"
    assert captured["port"] == 8123
    assert served_app.state.settings.internal_api_key == "custom-cli-secret"
    assert served_app.state.settings.callback_api_key == "outbound-hook-secret"
    assert served_app.state.settings.resolved_callback_allowed_origins == (
        "https://hooks.tta.local:443",
    )

    bad_env = tmp_path / "bad.env"
    bad_env.write_text("INTERNAL_API_KEY=change-me-local-dev-key\n", encoding="utf-8")
    with pytest.raises(ValueError, match="INTERNAL_API_KEY"):
        cli_main(["serve", "--env-file", str(bad_env)])


def test_submit_rejects_untrusted_callback_urls_and_isolates_callback_credentials(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    settings = Settings(
        _env_file=None,
        internal_api_key="inbound-api-secret",
        callback_api_key="outbound-callback-secret",
        callback_allowed_origins=["https://backend.tta.local"],
    )
    app = create_app(settings=settings, orchestrator=_build_mock_orchestrator())

    with TestClient(app) as client:
        for bad_url in (
            "https://evil.attacker.example/hook",
            "http://backend.tta.local/hook",
            "https://user@backend.tta.local/hook",
            "file:///etc/passwd",
            "   ",
        ):
            bad_req = sample_job_request.model_copy(
                update={"job_id": uuid4(), "callback_url": bad_url}
            )
            resp = client.post(
                "/v1/analysis/jobs",
                json=bad_req.model_dump(mode="json", by_alias=True),
                headers={"X-Internal-Api-Key": "inbound-api-secret"},
            )
            assert resp.status_code == 400

    captured_requests: list[httpx.Request] = []

    def transport_handler(request: httpx.Request) -> httpx.Response:
        captured_requests.append(request)
        return httpx.Response(200)

    mock_http = httpx.Client(transport=httpx.MockTransport(transport_handler))
    store = JobStore(
        orchestrator=_build_mock_orchestrator(),
        callback_allowed_origins=["https://backend.tta.local"],
        callback_api_key="outbound-callback-secret",
        http_client=mock_http,
    )
    try:
        good_req = sample_job_request.model_copy(
            update={"job_id": uuid4(), "callback_url": "https://backend.tta.local/api/callback"}
        )
        store.submit(good_req)
        for _ in range(40):
            if captured_requests:
                break
            time.sleep(0.05)

        assert len(captured_requests) == 1
        sent = captured_requests[0]
        header_keys = {k.lower(): v for k, v in sent.headers.items()}
        assert "x-internal-api-key" not in header_keys
        assert header_keys.get("x-callback-api-key") == "outbound-callback-secret"

        # Redirect from allowed origin must NOT be followed to an untrusted target
        redirect_targets: list[str] = []

        def redirect_handler(request: httpx.Request) -> httpx.Response:
            redirect_targets.append(str(request.url))
            return httpx.Response(307, headers={"Location": "https://evil.attacker.example/leak"})

        redirect_client = httpx.Client(
            transport=httpx.MockTransport(redirect_handler),
            follow_redirects=True,
        )
        redirect_store = JobStore(
            orchestrator=_build_mock_orchestrator(),
            callback_allowed_origins=["https://backend.tta.local"],
            callback_api_key="outbound-callback-secret",
            http_client=redirect_client,
        )
        try:
            redir_req = sample_job_request.model_copy(
                update={
                    "job_id": uuid4(),
                    "callback_url": "https://backend.tta.local/api/redirect",
                }
            )
            redirect_store.submit(redir_req)
            for _ in range(40):
                rec = redirect_store.get(redir_req.job_id)
                if rec is not None and rec.finalized:
                    break
                time.sleep(0.05)
            time.sleep(0.05)
            assert redirect_targets == ["https://backend.tta.local/api/redirect"]
        finally:
            redirect_store.shutdown()
            redirect_client.close()
    finally:
        store.shutdown()
        mock_http.close()


def test_stale_superseded_execution_cannot_mutate_active_record_or_send_callback(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    first_started = threading.Event()
    release_first = threading.Event()
    second_started = threading.Event()

    class BlockingThenSucceedingOrchestrator:
        def __init__(self) -> None:
            self._delegate = _build_mock_orchestrator()
            self._calls = 0

        def run(self, request, cancel_event=None, progress_callback=None):
            self._calls += 1
            call_num = self._calls
            if call_num == 1:
                first_started.set()
                release_first.wait(timeout=5.0)
                raise JobCancelledError("Cancelled first execution.")
            second_started.set()
            return self._delegate.run(
                request,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
            )

    callbacks: list[dict[str, object]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        import json

        callbacks.append(json.loads(request.content.decode("utf-8")))
        return httpx.Response(200)

    mock_http = httpx.Client(transport=httpx.MockTransport(handler))
    store = JobStore(
        orchestrator=BlockingThenSucceedingOrchestrator(),  # type: ignore[arg-type]
        callback_allowed_origins=["https://backend.tta.local"],
        max_workers=2,
        http_client=mock_http,
    )
    try:
        shared_job_id = uuid4()
        req1 = sample_job_request.model_copy(
            update={"job_id": shared_job_id, "callback_url": "https://backend.tta.local/cb1"}
        )
        req2 = sample_job_request.model_copy(
            update={"job_id": shared_job_id, "callback_url": "https://backend.tta.local/cb2"}
        )

        store.submit(req1)
        assert first_started.wait(timeout=3.0)
        assert store.cancel(shared_job_id) is True

        # Resubmit the same job_id while the first execution is still blocked
        store.submit(req2)
        assert second_started.wait(timeout=3.0)
        for _ in range(40):
            rec = store.get(shared_job_id)
            if rec is not None and rec.status == "completed":
                break
            time.sleep(0.05)

        # Unblock the stale first worker; it must NOT overwrite state or post a callback
        release_first.set()
        time.sleep(0.15)

        active = store.get(shared_job_id)
        assert active is not None
        assert active.status == "completed"
        assert active.draft is not None
        assert len(callbacks) == 1
        assert callbacks[0]["status"] == "completed"
    finally:
        release_first.set()
        store.shutdown()
        mock_http.close()


def test_prestart_cancelled_job_dispatches_single_cancelled_callback(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    blocker_started = threading.Event()
    release_blocker = threading.Event()

    class BlockerOrchestrator:
        def __init__(self) -> None:
            self._delegate = _build_mock_orchestrator()
            self._calls = 0

        def run(self, request, cancel_event=None, progress_callback=None):
            self._calls += 1
            if self._calls == 1:
                blocker_started.set()
                release_blocker.wait(timeout=5.0)
            return self._delegate.run(
                request,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
            )

    callbacks: list[dict[str, object]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        import json

        callbacks.append(json.loads(request.content.decode("utf-8")))
        return httpx.Response(200)

    mock_http = httpx.Client(transport=httpx.MockTransport(handler))
    store = JobStore(
        orchestrator=BlockerOrchestrator(),  # type: ignore[arg-type]
        callback_allowed_origins=["https://backend.tta.local"],
        max_workers=1,
        http_client=mock_http,
    )
    try:
        blocker_req = sample_job_request.model_copy(
            update={"job_id": uuid4(), "callback_url": None}
        )
        queued_req = sample_job_request.model_copy(
            update={"job_id": uuid4(), "callback_url": "https://backend.tta.local/cb"}
        )

        store.submit(blocker_req)
        assert blocker_started.wait(timeout=3.0)

        # Single worker is busy, so queued_req remains queued before cancellation
        store.submit(queued_req)
        assert store.cancel(queued_req.job_id) is True

        release_blocker.set()
        for _ in range(40):
            if callbacks:
                break
            time.sleep(0.05)

        assert len(callbacks) == 1
        assert callbacks[0]["jobId"] == str(queued_req.job_id)
        assert callbacks[0]["status"] == "cancelled"
        assert callbacks[0]["draft"] is None
    finally:
        release_blocker.set()
        store.shutdown()
        mock_http.close()


def test_job_store_prunes_terminal_jobs_by_count_and_ttl(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    store = JobStore(
        orchestrator=_build_mock_orchestrator(),
        max_workers=1,
        max_retained_terminal_jobs=2,
        terminal_job_ttl_sec=60.0,
    )
    try:
        job_ids = [uuid4() for _ in range(4)]
        for jid in job_ids:
            req = sample_job_request.model_copy(update={"job_id": jid, "callback_url": None})
            store.submit(req)
            for _ in range(40):
                rec = store.get(jid)
                if rec is not None and rec.finalized:
                    break
                time.sleep(0.02)

        # Only the 2 most recent terminal jobs should remain
        assert store.get(job_ids[0]) is None
        assert store.get(job_ids[1]) is None
        assert store.get(job_ids[2]) is not None
        assert store.get(job_ids[3]) is not None

        # Backdate job_ids[2] beyond TTL and verify it is pruned on lookup
        rec_2 = store.get(job_ids[2])
        assert rec_2 is not None
        rec_2.updated_at = rec_2.updated_at - timedelta(seconds=120)
        assert store.get(job_ids[2]) is None
        assert store.get(job_ids[3]) is not None
    finally:
        store.shutdown()
