"""Security, CLI serve, callback trust, concurrency, and retention tests."""

from __future__ import annotations

import json
import logging
import socket
import threading
import time
from contextlib import contextmanager
from datetime import timedelta
from pathlib import Path
from uuid import uuid4

import httpx
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import video_analyzer.api.app as app_module
from tests.test_orchestrator_and_api import (
    MockCapResolver,
    MockDetector,
    MockFFmpegClient,
    MockPass1Analyzer,
    _build_mock_orchestrator,
)
from video_analyzer.api.app import create_app
from video_analyzer.api.job_store import JobStore
from video_analyzer.cli import main as cli_main
from video_analyzer.config import Settings
from video_analyzer.media.ffmpeg_client import FFmpegClient
from video_analyzer.pipeline.orchestrator import JobCancelledError, VideoAnalysisOrchestrator
from video_analyzer.schemas.request import AnalyzeJobRequest, VideoSource


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
    mock_orchestrator = _build_mock_orchestrator()

    def fake_build_orchestrator(settings: Settings):
        captured["settings"] = settings
        return mock_orchestrator

    monkeypatch.setattr(
        "video_analyzer.api.app.build_default_orchestrator",
        fake_build_orchestrator,
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
    assert captured["settings"].internal_api_key == "custom-cli-secret"  # type: ignore[union-attr]
    assert served_app.state.job_store._orchestrator is mock_orchestrator
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


def test_resubmission_and_eviction_serialize_with_callback_delivery(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    import json

    callbacks: list[dict[str, object]] = []

    def handler(request: httpx.Request) -> httpx.Response:
        callbacks.append(json.loads(request.content.decode("utf-8")))
        return httpx.Response(200)

    mock_http = httpx.Client(transport=httpx.MockTransport(handler))
    store = JobStore(
        orchestrator=_build_mock_orchestrator(),
        callback_allowed_origins=["https://backend.tta.local"],
        max_workers=2,
        max_retained_terminal_jobs=0,
        terminal_job_ttl_sec=0.0,
        http_client=mock_http,
    )

    first_dispatch_entered = threading.Event()
    allow_first_dispatch = threading.Event()
    original_dispatch = store._dispatch_callback
    dispatch_count = 0

    def controlled_dispatch(callback_url: str, payload: dict[str, object], job_id) -> None:
        nonlocal dispatch_count
        dispatch_count += 1
        if dispatch_count == 1:
            first_dispatch_entered.set()
            assert allow_first_dispatch.wait(timeout=5.0)
        original_dispatch(callback_url, payload, job_id)

    store._dispatch_callback = controlled_dispatch  # type: ignore[method-assign]

    resubmit_thread: threading.Thread | None = None
    try:
        shared_job_id = uuid4()
        req1 = sample_job_request.model_copy(
            update={"job_id": shared_job_id, "callback_url": "https://backend.tta.local/cb1"}
        )
        req2 = sample_job_request.model_copy(
            update={"job_id": shared_job_id, "callback_url": "https://backend.tta.local/cb2"}
        )

        rec1 = store.submit(req1)
        assert first_dispatch_entered.wait(timeout=3.0)

        # Even with retention=0/ttl=0, in-flight callback keeps rec1 un-evicted
        assert store.get(shared_job_id) is rec1

        resubmit_done = threading.Event()
        resubmitted: list[object] = []

        def do_resubmit() -> None:
            resubmitted.append(store.submit(req2))
            resubmit_done.set()

        resubmit_thread = threading.Thread(target=do_resubmit)
        resubmit_thread.start()

        # Replacement of the same job_id must block until callback 1 finishes delivery
        assert not resubmit_done.wait(timeout=0.15)
        assert callbacks == []

        allow_first_dispatch.set()
        assert resubmit_done.wait(timeout=3.0)
        resubmit_thread.join(timeout=3.0)

        rec2 = resubmitted[0]
        assert rec2 is not rec1
        assert rec1.execution_id != rec2.execution_id  # type: ignore[attr-defined]

        for _ in range(40):
            if len(callbacks) == 2 and rec2.finalized:  # type: ignore[attr-defined]
                break
            time.sleep(0.05)

        assert len(callbacks) == 2
        assert callbacks[0]["jobId"] == str(shared_job_id)
        assert callbacks[0]["executionId"] == str(rec1.execution_id)
        assert callbacks[1]["jobId"] == str(shared_job_id)
        assert callbacks[1]["executionId"] == str(
            rec2.execution_id  # type: ignore[attr-defined]
        )
    finally:
        allow_first_dispatch.set()
        if resubmit_thread is not None and resubmit_thread.is_alive():
            resubmit_thread.join(timeout=2.0)
        store.shutdown()
        mock_http.close()


def test_job_store_snapshots_are_atomic_and_isolated_from_subsequent_updates(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    gate = threading.Event()
    started = threading.Event()

    class GatedOrchestrator:
        def __init__(self) -> None:
            self._delegate = _build_mock_orchestrator()

        def run(self, request, cancel_event=None, progress_callback=None):
            if progress_callback:
                progress_callback("pass1_vlm", 30)
            started.set()
            gate.wait(timeout=5.0)
            return self._delegate.run(
                request,
                cancel_event=cancel_event,
                progress_callback=progress_callback,
            )

    store = JobStore(orchestrator=GatedOrchestrator())  # type: ignore[arg-type]
    try:
        req = sample_job_request.model_copy(update={"job_id": uuid4(), "callback_url": None})
        initial = store.submit_and_snapshot(req)
        assert initial.status in ("queued", "processing")
        assert started.wait(timeout=3.0)

        mid = store.get_snapshot(req.job_id)
        assert mid is not None
        assert mid.status == "processing"
        assert mid.stage == "pass1_vlm"
        assert mid.progress_percent == 30
        assert mid.draft is None

        gate.set()
        for _ in range(40):
            done = store.get_snapshot(req.job_id)
            if done is not None and done.status == "completed":
                break
            time.sleep(0.05)

        # Prior mid-flight snapshot must remain unaltered after completion
        assert mid.status == "processing"
        assert mid.draft is None
        final_snap = store.get_snapshot(req.job_id)
        assert final_snap is not None
        assert final_snap.status == "completed"
        assert final_snap.progress_percent == 100
        assert final_snap.draft is not None
    finally:
        gate.set()
        store.shutdown()


def test_job_store_shutdown_signals_active_jobs_to_cancel(
    sample_job_request: AnalyzeJobRequest,
) -> None:
    started = threading.Event()
    saw_cancel_signal = threading.Event()

    class CooperativeOrchestrator:
        def run(self, request, cancel_event=None, progress_callback=None):
            started.set()
            assert cancel_event is not None
            if cancel_event.wait(timeout=5.0):
                saw_cancel_signal.set()
                raise JobCancelledError("Cancelled on shutdown.")
            raise AssertionError("Expected cancel_event to be signaled on shutdown.")

    completed_store = JobStore(orchestrator=_build_mock_orchestrator(), max_workers=1)
    try:
        done_req = sample_job_request.model_copy(update={"job_id": uuid4(), "callback_url": None})
        completed_store.submit(done_req)
        for _ in range(40):
            rec = completed_store.get(done_req.job_id)
            if rec is not None and rec.finalized:
                break
            time.sleep(0.02)
        completed_store.shutdown()
        done_rec = completed_store.get(done_req.job_id)
        assert done_rec is not None
        assert done_rec.status == "completed"
    finally:
        completed_store.shutdown()

    store = JobStore(
        orchestrator=CooperativeOrchestrator(),  # type: ignore[arg-type]
        max_workers=1,
    )
    active_req = sample_job_request.model_copy(update={"job_id": uuid4(), "callback_url": None})
    rec = store.submit(active_req)
    assert started.wait(timeout=3.0)

    store.shutdown()
    assert saw_cancel_signal.wait(timeout=3.0)
    assert rec.cancel_event.is_set()
    assert rec.status == "cancelled"
    assert rec.stage == "cancelled"


def test_job_store_sanitizes_callback_failure_logs_without_altering_delivery_url(
    sample_job_request: AnalyzeJobRequest,
    caplog: pytest.LogCaptureFixture,
) -> None:
    secret_token = "super-secret-callback-token"
    secret_password = "super-secret-password"
    delivered_urls: list[str] = []

    def fail_status_handler(request: httpx.Request) -> httpx.Response:
        delivered_urls.append(str(request.url))
        return httpx.Response(500, request=request, text=f"upstream leak token={secret_token}")

    client_500 = httpx.Client(transport=httpx.MockTransport(fail_status_handler))
    store_500 = JobStore(
        orchestrator=_build_mock_orchestrator(),
        callback_allowed_origins=["https://backend.tta.local"],
        http_client=client_500,
    )
    try:
        callback_url = f"https://backend.tta.local/api/hook?token={secret_token}"
        with caplog.at_level(logging.WARNING, logger="video_analyzer.api.job_store"):
            req = sample_job_request.model_copy(
                update={"job_id": uuid4(), "callback_url": callback_url}
            )
            store_500.submit(req)
            for _ in range(40):
                rec = store_500.get(req.job_id)
                if rec is not None and rec.finalized:
                    break
                time.sleep(0.05)

        assert delivered_urls == [callback_url]
        assert "https://backend.tta.local/api/hook" in caplog.text
        assert "HTTPStatusError (HTTP 500)" in caplog.text
        assert secret_token not in caplog.text

        caplog.clear()

        def fail_connect_handler(request: httpx.Request) -> httpx.Response:
            raise httpx.ConnectError(f"failed connecting to {request.url}", request=request)

        client_conn = httpx.Client(transport=httpx.MockTransport(fail_connect_handler))
        store_conn = JobStore(
            orchestrator=_build_mock_orchestrator(),
            callback_allowed_origins=["https://backend.tta.local"],
            http_client=client_conn,
        )
        try:
            with caplog.at_level(logging.WARNING, logger="video_analyzer.api.job_store"):
                store_conn._dispatch_callback(
                    f"https://backend.tta.local/api/cb?token={secret_token}",
                    {"status": "completed"},
                    uuid4(),
                )
                store_conn._dispatch_callback(
                    f"https://user:{secret_password}@backend.tta.local/api/cb"
                    f"?token={secret_token}#frag",
                    {"status": "completed"},
                    uuid4(),
                )
            assert "ConnectError" in caplog.text
            assert "ValueError" in caplog.text
            assert "https://backend.tta.local/api/cb" in caplog.text
            assert secret_token not in caplog.text
            assert secret_password not in caplog.text
            assert "#frag" not in caplog.text
        finally:
            store_conn.shutdown()
            client_conn.close()
    finally:
        store_500.shutdown()
        client_500.close()


def test_cli_fails_on_missing_explicit_env_file_and_keeps_default_env_fallback(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    capsys: pytest.CaptureFixture[str],
) -> None:
    missing_env = tmp_path / "does_not_exist.env"
    exit_serve = cli_main(["serve", "--env-file", str(missing_env)])
    assert exit_serve == 2
    assert "env file not found" in capsys.readouterr().err

    video = tmp_path / "sample.mp4"
    video.write_bytes(b"video")
    snapshot_file = (
        Path(__file__).resolve().parents[1]
        / "sample_snapshots"
        / "waterpolo_default_snapshot.json"
    )
    exit_analyze = cli_main(
        [
            "analyze",
            "--video",
            str(video),
            "--snapshot",
            str(snapshot_file),
            "--out",
            str(tmp_path / "out.json"),
            "--env-file",
            str(missing_env),
        ]
    )
    assert exit_analyze == 2
    assert "env file not found" in capsys.readouterr().err

    # Without --env-file and with no .env in cwd, default Settings() still applies
    no_env_dir = tmp_path / "empty_cwd"
    no_env_dir.mkdir()
    monkeypatch.chdir(no_env_dir)
    monkeypatch.setenv("INTERNAL_API_KEY", "from-process-env-secret")
    monkeypatch.setattr(
        "video_analyzer.api.app.build_default_orchestrator",
        lambda settings: _build_mock_orchestrator(work_dir=tmp_path),
    )
    served: dict[str, object] = {}
    monkeypatch.setattr(
        "video_analyzer.cli.uvicorn.run",
        lambda app, host, port: served.update({"app": app, "host": host, "port": port}),
    )
    assert cli_main(["serve"]) == 0
    assert isinstance(served["app"], FastAPI)


def test_job_store_sanitizes_presigned_download_http_failure_and_supports_mid_download_cancel(
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
    caplog: pytest.LogCaptureFixture,
) -> None:
    signed_url = (
        "https://cdn.tta.example/videos/match.mp4?X-Amz-Signature=secret-sig-999&token=tok-888"
    )
    downloaded_urls: list[str] = []
    callbacks: list[dict[str, object]] = []

    def fail_download_handler(request: httpx.Request) -> httpx.Response:
        downloaded_urls.append(str(request.url))
        return httpx.Response(403, request=request, text="AccessDenied")

    def callback_handler(request: httpx.Request) -> httpx.Response:
        callbacks.append(json.loads(request.content.decode("utf-8")))
        return httpx.Response(200)

    dl_client = httpx.Client(transport=httpx.MockTransport(fail_download_handler))
    cb_client = httpx.Client(transport=httpx.MockTransport(callback_handler))
    work_dir = tmp_path / "work"
    work_dir.mkdir()
    orchestrator = VideoAnalysisOrchestrator(
        settings=Settings(_env_file=None, internal_api_key="test-key", work_dir=str(work_dir)),
        ffmpeg_client=FFmpegClient(http_client=dl_client),
        pass1_analyzer=MockPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=MockCapResolver(),
    )
    store = JobStore(
        orchestrator=orchestrator,
        callback_allowed_origins=["https://backend.tta.local"],
        http_client=cb_client,
    )
    try:
        req = sample_job_request.model_copy(
            update={
                "job_id": uuid4(),
                "video_source": VideoSource(kind="presigned_url", uri=signed_url),
                "callback_url": "https://backend.tta.local/api/cb",
            }
        )
        with caplog.at_level(logging.ERROR, logger="video_analyzer.api.job_store"):
            store.submit(req)
            for _ in range(50):
                rec = store.get(req.job_id)
                if rec is not None and rec.finalized:
                    break
                time.sleep(0.05)

        snap = store.get_snapshot(req.job_id)
        assert snap is not None
        assert snap.status == "failed"
        assert snap.error_message is not None
        assert downloaded_urls == [signed_url]
        assert len(callbacks) == 1
        cb_error = str(callbacks[0]["errorMessage"])
        assert "HTTP 403" in snap.error_message
        assert "HTTP 403" in cb_error
        for secret in ("secret-sig-999", "tok-888", "cdn.tta.example"):
            assert secret not in caplog.text
            assert secret not in snap.error_message
            assert secret not in cb_error

        # Defense-in-depth: raw httpx.HTTPStatusError escaping orchestrator is also sanitized
        caplog.clear()
        callbacks.clear()

        class RawHttpErrorOrchestrator:
            def run(self, request, cancel_event=None, progress_callback=None):
                req_obj = httpx.Request("GET", signed_url)
                resp_obj = httpx.Response(401, request=req_obj)
                raise httpx.HTTPStatusError(
                    f"Client error '401 Unauthorized' for url '{signed_url}'",
                    request=req_obj,
                    response=resp_obj,
                )

        raw_store = JobStore(
            orchestrator=RawHttpErrorOrchestrator(),  # type: ignore[arg-type]
            callback_allowed_origins=["https://backend.tta.local"],
            http_client=cb_client,
        )
        try:
            raw_req = req.model_copy(update={"job_id": uuid4()})
            with caplog.at_level(logging.ERROR, logger="video_analyzer.api.job_store"):
                raw_store.submit(raw_req)
                for _ in range(50):
                    rec = raw_store.get(raw_req.job_id)
                    if rec is not None and rec.finalized:
                        break
                    time.sleep(0.05)
            raw_snap = raw_store.get_snapshot(raw_req.job_id)
            assert raw_snap is not None
            assert raw_snap.status == "failed"
            assert raw_snap.error_message is not None
            assert "HTTP 401" in raw_snap.error_message
            assert len(callbacks) == 1
            raw_cb_error = str(callbacks[0]["errorMessage"])
            assert "HTTP 401" in raw_cb_error
            for secret in ("secret-sig-999", "tok-888", "cdn.tta.example"):
                assert secret not in caplog.text
                assert secret not in raw_snap.error_message
                assert secret not in raw_cb_error
        finally:
            raw_store.shutdown()

        # Active cancellation mid-download transitions to cancelled and deletes partial file
        callbacks.clear()
        chunk_started = threading.Event()
        release_second_chunk = threading.Event()

        class GatedDownloadStream(httpx.SyncByteStream):
            def __init__(self) -> None:
                self.closed = False

            def __iter__(self):
                yield b"chunk-1"
                chunk_started.set()
                release_second_chunk.wait(timeout=5.0)
                yield b"chunk-2"

            def close(self) -> None:
                self.closed = True

        gated_stream = GatedDownloadStream()
        cancel_dl_client = httpx.Client(
            transport=httpx.MockTransport(
                lambda request: httpx.Response(200, stream=gated_stream, request=request)
            )
        )
        cancel_work_dir = tmp_path / "cancel_work"
        cancel_work_dir.mkdir()
        cancel_orch = VideoAnalysisOrchestrator(
            settings=Settings(
                _env_file=None, internal_api_key="test-key", work_dir=str(cancel_work_dir)
            ),
            ffmpeg_client=FFmpegClient(http_client=cancel_dl_client),
            pass1_analyzer=MockPass1Analyzer(),
            detector=MockDetector(),
            cap_resolver=MockCapResolver(),
        )
        cancel_store = JobStore(
            orchestrator=cancel_orch,
            callback_allowed_origins=["https://backend.tta.local"],
            http_client=cb_client,
        )
        try:
            cancel_req = req.model_copy(update={"job_id": uuid4()})
            cancel_store.submit(cancel_req)
            assert chunk_started.wait(timeout=3.0)
            assert cancel_store.cancel(cancel_req.job_id) is True
            release_second_chunk.set()
            for _ in range(50):
                rec = cancel_store.get(cancel_req.job_id)
                if rec is not None and rec.finalized:
                    break
                time.sleep(0.05)

            final_rec = cancel_store.get(cancel_req.job_id)
            assert final_rec is not None
            assert final_rec.status == "cancelled"
            assert gated_stream.closed is True
            assert len(callbacks) == 1
            assert callbacks[0]["status"] == "cancelled"
            assert list(cancel_work_dir.rglob("source_video.mp4")) == []
        finally:
            release_second_chunk.set()
            cancel_store.shutdown()
            cancel_dl_client.close()
    finally:
        store.shutdown()
        dl_client.close()
        cb_client.close()


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


def test_job_store_cancels_slow_header_download_releases_worker_and_cleans_up(
    monkeypatch: pytest.MonkeyPatch,
    sample_job_request: AnalyzeJobRequest,
    tmp_path: Path,
) -> None:
    for var in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"):
        monkeypatch.delenv(var, raising=False)

    mock_ffmpeg = MockFFmpegClient()

    class RealDownloadMockPipelineFFmpeg(FFmpegClient):
        def probe(self, video_path: Path, cancel_event=None):
            return mock_ffmpeg.probe(video_path, cancel_event=cancel_event)

        def create_pass1_chunks(self, video_path, probe, output_dir, **kwargs):
            return mock_ffmpeg.create_pass1_chunks(video_path, probe, output_dir, **kwargs)

        def extract_window_frames(self, video_path, center_sec, **kwargs):
            return mock_ffmpeg.extract_window_frames(video_path, center_sec, **kwargs)

    callbacks: list[dict[str, object]] = []
    cb_client = httpx.Client(
        transport=httpx.MockTransport(
            lambda request: (
                callbacks.append(json.loads(request.content.decode("utf-8"))),
                httpx.Response(200),
            )[1]
        )
    )
    work_dir = tmp_path / "slow_hdr_work"
    work_dir.mkdir()
    orchestrator = VideoAnalysisOrchestrator(
        settings=Settings(_env_file=None, internal_api_key="test-key", work_dir=str(work_dir)),
        ffmpeg_client=RealDownloadMockPipelineFFmpeg(
            download_read_timeout_sec=10.0,
            download_total_timeout_sec=300.0,
        ),
        pass1_analyzer=MockPass1Analyzer(),
        detector=MockDetector(),
        cap_resolver=MockCapResolver(),
    )
    store = JobStore(
        orchestrator=orchestrator,
        callback_allowed_origins=["https://backend.tta.local"],
        max_workers=1,
        http_client=cb_client,
    )
    try:
        with _slow_header_http_server() as (port, headers_started, peer_closed):
            job1 = sample_job_request.model_copy(
                update={
                    "job_id": uuid4(),
                    "video_source": VideoSource(
                        kind="presigned_url",
                        uri=f"http://127.0.0.1:{port}/match.mp4?sig=slow-hdr-secret",
                    ),
                    "callback_url": "https://backend.tta.local/api/cb1",
                }
            )
            store.submit(job1)
            assert headers_started.wait(timeout=3.0)

            cancel_started = time.monotonic()
            assert store.cancel(job1.job_id) is True
            for _ in range(40):
                rec1 = store.get(job1.job_id)
                if rec1 is not None and rec1.finalized:
                    break
                time.sleep(0.05)

            assert time.monotonic() - cancel_started < 2.0
            rec1 = store.get(job1.job_id)
            assert rec1 is not None
            assert rec1.status == "cancelled"
            assert peer_closed.wait(timeout=2.0) is True
            assert list(work_dir.iterdir()) == []

        # Single worker thread was released and promptly completes a subsequent job
        job2 = sample_job_request.model_copy(
            update={
                "job_id": uuid4(),
                "callback_url": "https://backend.tta.local/api/cb2",
            }
        )
        store.submit(job2)
        for _ in range(60):
            rec2 = store.get(job2.job_id)
            if rec2 is not None and rec2.finalized:
                break
            time.sleep(0.05)

        rec2 = store.get(job2.job_id)
        assert rec2 is not None
        assert rec2.status == "completed"
        assert rec2.draft is not None
        assert len(callbacks) == 2
        assert callbacks[0]["jobId"] == str(job1.job_id)
        assert callbacks[0]["status"] == "cancelled"
        assert callbacks[1]["jobId"] == str(job2.job_id)
        assert callbacks[1]["status"] == "completed"
    finally:
        store.shutdown()
        cb_client.close()
