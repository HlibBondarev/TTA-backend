"""Offline tests for snapshot loading (file + mocked HTTP) and the CLI analyze preflight."""

from __future__ import annotations

import json
from pathlib import Path
from uuid import UUID

import httpx
import pytest

from tests.support import SAMPLE_SNAPSHOT_PATH
from tests.test_orchestrator_and_api import _build_mock_orchestrator
from video_analyzer.cli import main as cli_main
from video_analyzer.schemas.request import AnalyzeJobRequest, MatchSnapshot
from video_analyzer.snapshot_source import (
    SnapshotSourceError,
    build_snapshot_url,
    fetch_snapshot,
    load_snapshot_file,
)

BASE_URL = "http://tta.test"
MATCH_ID = UUID("30000000-0000-0000-0000-000000000001")
TEAM_ID = UUID("aaaaaaaa-bbbb-cccc-dddd-000000000001")
EXPECTED_PATH = f"/api/matches/{MATCH_ID}/teams/{TEAM_ID}/analysis-snapshot"


def _sample_text() -> str:
    return SAMPLE_SNAPSHOT_PATH.read_text(encoding="utf-8")


def _client(handler) -> httpx.Client:
    return httpx.Client(transport=httpx.MockTransport(handler))


def _ok_handler(captured: list[httpx.Request] | None = None):
    def handler(request: httpx.Request) -> httpx.Response:
        if captured is not None:
            captured.append(request)
        return httpx.Response(200, json=json.loads(_sample_text()))

    return handler


def test_fetch_snapshot_parses_the_net_response_and_calls_the_expected_endpoint() -> None:
    captured: list[httpx.Request] = []

    snapshot = fetch_snapshot(
        f"{BASE_URL}/", MATCH_ID, TEAM_ID, client=_client(_ok_handler(captured))
    )

    assert isinstance(snapshot, MatchSnapshot)
    assert len(snapshot.lineup) == 15
    assert [r.method for r in captured] == ["GET"]
    assert captured[0].url.path == EXPECTED_PATH
    assert captured[0].url.host == "tta.test"


@pytest.mark.parametrize(
    ("status", "body"),
    [(400, "bad ids"), (404, "Match not found"), (500, "")],
)
def test_fetch_snapshot_reports_http_error_statuses(status: int, body: str) -> None:
    client = _client(lambda request: httpx.Response(status, text=body))
    with pytest.raises(SnapshotSourceError, match=f"HTTP {status}"):
        fetch_snapshot(BASE_URL, MATCH_ID, TEAM_ID, client=client)


def test_fetch_snapshot_reports_network_errors_invalid_json_and_contract_mismatch() -> None:
    def refuse(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("connection refused", request=request)

    refusing = _client(refuse)
    with pytest.raises(SnapshotSourceError, match="request to the snapshot API failed"):
        fetch_snapshot(BASE_URL, MATCH_ID, TEAM_ID, client=refusing)

    html = _client(lambda request: httpx.Response(200, text="<html>not json</html>"))
    with pytest.raises(SnapshotSourceError, match="not valid JSON"):
        fetch_snapshot(BASE_URL, MATCH_ID, TEAM_ID, client=html)

    legacy = _client(lambda request: httpx.Response(200, json={"trackedTeam": {}}))
    with pytest.raises(SnapshotSourceError, match="analysis-snapshot contract"):
        fetch_snapshot(BASE_URL, MATCH_ID, TEAM_ID, client=legacy)


@pytest.mark.parametrize("bad_url", ["", "ftp://tta.test", "tta.test", "file:///etc/passwd"])
def test_build_snapshot_url_rejects_non_http_base_urls(bad_url: str) -> None:
    with pytest.raises(SnapshotSourceError, match=r"http\(s\) URL"):
        build_snapshot_url(bad_url, MATCH_ID, TEAM_ID)


def _recording_client() -> tuple[httpx.Client, list[httpx.Request]]:
    sent: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        sent.append(request)
        return httpx.Response(200, json={})

    return _client(handler), sent


@pytest.mark.parametrize("bad_id", ["", "not-a-uuid", "../../etc/passwd", f"{MATCH_ID}/../x"])
def test_fetch_snapshot_rejects_invalid_uuids_without_sending_a_request(bad_id: str) -> None:
    client, sent = _recording_client()
    with pytest.raises(SnapshotSourceError, match="valid UUID"):
        fetch_snapshot(BASE_URL, bad_id, TEAM_ID, client=client)
    with pytest.raises(SnapshotSourceError, match="valid UUID"):
        fetch_snapshot(BASE_URL, MATCH_ID, bad_id, client=client)
    assert sent == []


@pytest.mark.parametrize(
    ("bad_url", "message"),
    [
        ("ftp://tta.test", "http"),
        ("file:///etc/passwd", "http"),
        ("http://", "http"),
        ("tta.test", "http"),
        ("http://user:pw@tta.test", "credentials"),
        ("http://user@tta.test", "credentials"),
        ("http://tta.test?x=1", "query"),
        ("http://tta.test#frag", "query"),
        ("http://tta.test/;p=1", "query"),
        ("http://tta.test:99999", "malformed"),
        ("http://tta.test:abc", "malformed"),
        ("http://[::1", "malformed"),
    ],
)
def test_fetch_snapshot_rejects_unsafe_base_urls_without_sending_a_request(
    bad_url: str, message: str
) -> None:
    client, sent = _recording_client()
    with pytest.raises(SnapshotSourceError, match=message):
        fetch_snapshot(bad_url, MATCH_ID, TEAM_ID, client=client)
    assert sent == []


def test_fetch_snapshot_translates_httpx_invalid_url() -> None:
    def invalid(request: httpx.Request) -> httpx.Response:
        raise httpx.InvalidURL("bad url")

    broken = _client(invalid)
    with pytest.raises(SnapshotSourceError, match="invalid"):
        fetch_snapshot(BASE_URL, MATCH_ID, TEAM_ID, client=broken)


def test_fetch_snapshot_never_follows_redirects() -> None:
    hosts: list[str] = []

    def redirect(request: httpx.Request) -> httpx.Response:
        hosts.append(request.url.host)
        return httpx.Response(302, headers={"location": "http://169.254.169.254/latest"})

    redirecting = httpx.Client(transport=httpx.MockTransport(redirect), follow_redirects=True)
    with pytest.raises(SnapshotSourceError, match="HTTP 302"):
        fetch_snapshot(BASE_URL, MATCH_ID, TEAM_ID, client=redirecting)
    assert hosts == ["tta.test"]


def test_build_snapshot_url_normalizes_base_and_uses_canonical_uuid_path() -> None:
    url = build_snapshot_url("HTTP://Tta.Test:8080/root/", str(MATCH_ID).upper(), TEAM_ID)
    assert url == (
        f"http://tta.test:8080/root/api/matches/{MATCH_ID}/teams/{TEAM_ID}/analysis-snapshot"
    )


def test_cli_reports_malformed_api_base_url_cleanly_without_a_request(
    capsys: pytest.CaptureFixture[str], cli_env: dict[str, object]
) -> None:
    for bad_url in ("http://tta.test:99999", "http://u:p@tta.test", "ftp://tta.test"):
        exit_code = cli_main(
            _analyze_args(
                cli_env,
                "--match-id",
                str(MATCH_ID),
                "--team-id",
                str(TEAM_ID),
                "--api-base-url",
                bad_url,
            )
        )
        err = capsys.readouterr().err
        assert exit_code == 2
        assert err.startswith("Error:")
        assert "Traceback" not in err
    assert cli_env["requests"] == []


def test_load_snapshot_file_reports_unreadable_and_invalid_files(tmp_path: Path) -> None:
    assert load_snapshot_file(SAMPLE_SNAPSHOT_PATH).match.id == MATCH_ID
    broken = tmp_path / "broken.json"
    broken.write_text("{not json", encoding="utf-8")
    with pytest.raises(SnapshotSourceError, match="not valid JSON"):
        load_snapshot_file(broken)
    with pytest.raises(SnapshotSourceError, match="cannot read"):
        load_snapshot_file(tmp_path / "missing.json")


# ------------------------------------------------------------------ CLI


@pytest.fixture
def cli_env(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> dict[str, object]:
    """Isolate the CLI from real settings, .env files, ffmpeg, and Gemini."""
    monkeypatch.chdir(tmp_path)
    monkeypatch.delenv("TTA_API_BASE_URL", raising=False)
    video = tmp_path / "match.mp4"
    video.write_bytes(b"video")
    built: list[AnalyzeJobRequest] = []

    class SpyOrchestrator:
        def __init__(self) -> None:
            self._inner = _build_mock_orchestrator(work_dir=tmp_path)

        def run(self, request: AnalyzeJobRequest, **kwargs):
            built.append(request)
            return self._inner.run(request, **kwargs)

    monkeypatch.setattr(
        "video_analyzer.cli.build_default_orchestrator", lambda s: SpyOrchestrator()
    )
    return {"video": video, "out": tmp_path / "out" / "draft.json", "requests": built}


def _analyze_args(env: dict[str, object], *extra: str) -> list[str]:
    return ["analyze", "--video", str(env["video"]), "--out", str(env["out"]), *extra]


def test_cli_fetches_snapshot_over_mocked_http_and_passes_runtime_hints(
    monkeypatch: pytest.MonkeyPatch, cli_env: dict[str, object]
) -> None:
    captured: list[httpx.Request] = []

    def fake_fetch(base_url, match_id, team_id, timeout_sec=30.0, client=None):
        assert client is None
        return fetch_snapshot(
            base_url,
            match_id,
            team_id,
            timeout_sec,
            client=_client(_ok_handler(captured)),
        )

    monkeypatch.setattr("video_analyzer.cli.fetch_snapshot", fake_fetch)

    exit_code = cli_main(
        _analyze_args(
            cli_env,
            "--match-id",
            str(MATCH_ID),
            "--team-id",
            str(TEAM_ID),
            "--api-base-url",
            BASE_URL,
            "--tracked-cap-color",
            "white",
            "--opponent-cap-color",
            "blue",
        )
    )

    assert exit_code == 0
    assert captured[0].url.path == EXPECTED_PATH
    request = cli_env["requests"][0]
    assert request.match_id == MATCH_ID
    assert request.tracked_team_id == TEAM_ID
    assert request.runtime_hints.tracked_cap_color_hint == "white"
    assert request.runtime_hints.opponent_cap_color_hint == "blue"
    assert Path(str(cli_env["out"])).is_file()


def test_cli_uses_api_base_url_from_environment_settings(
    monkeypatch: pytest.MonkeyPatch, cli_env: dict[str, object]
) -> None:
    seen: list[str] = []

    def fake_fetch(base_url, match_id, team_id, timeout_sec=30.0, client=None):
        seen.append(base_url)
        return load_snapshot_file(SAMPLE_SNAPSHOT_PATH)

    monkeypatch.setattr("video_analyzer.cli.fetch_snapshot", fake_fetch)
    monkeypatch.setenv("TTA_API_BASE_URL", "http://env.tta.test")

    exit_code = cli_main(
        _analyze_args(cli_env, "--match-id", str(MATCH_ID), "--team-id", str(TEAM_ID))
    )

    assert exit_code == 0
    assert seen == ["http://env.tta.test"]


@pytest.mark.parametrize(
    ("extra", "message"),
    [
        (["--snapshot", "x.json", "--match-id", str(MATCH_ID)], "cannot be combined"),
        ([], "either --snapshot"),
        (["--match-id", str(MATCH_ID)], "either --snapshot"),
        (["--match-id", str(MATCH_ID), "--team-id", str(TEAM_ID)], "--api-base-url"),
    ],
)
def test_cli_rejects_conflicting_or_incomplete_snapshot_sources(
    extra: list[str],
    message: str,
    capsys: pytest.CaptureFixture[str],
    cli_env: dict[str, object],
) -> None:
    exit_code = cli_main(_analyze_args(cli_env, *extra))

    assert exit_code == 2
    err = capsys.readouterr().err
    assert message in err
    assert "Traceback" not in err
    assert cli_env["requests"] == []


def test_cli_reports_http_failures_without_stack_trace(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    cli_env: dict[str, object],
) -> None:
    def fake_fetch(base_url, match_id, team_id, timeout_sec=30.0, client=None):
        return fetch_snapshot(
            base_url,
            match_id,
            team_id,
            timeout_sec,
            client=_client(lambda request: httpx.Response(404, text="Match not found")),
        )

    monkeypatch.setattr("video_analyzer.cli.fetch_snapshot", fake_fetch)

    exit_code = cli_main(
        _analyze_args(
            cli_env,
            "--match-id",
            str(MATCH_ID),
            "--team-id",
            str(TEAM_ID),
            "--api-base-url",
            BASE_URL,
        )
    )

    err = capsys.readouterr().err
    assert exit_code == 2
    assert "HTTP 404" in err
    assert "Traceback" not in err
    assert cli_env["requests"] == []


def test_cli_fails_fast_on_empty_lineup_before_any_pipeline_work(
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    tmp_path: Path,
    cli_env: dict[str, object],
) -> None:
    def forbidden(settings):
        raise AssertionError("the orchestrator (ffmpeg/Gemini) must not be built")

    monkeypatch.setattr("video_analyzer.cli.build_default_orchestrator", forbidden)
    raw = json.loads(_sample_text())
    raw["lineup"] = []
    snapshot_file = tmp_path / "empty_lineup.json"
    snapshot_file.write_text(json.dumps(raw), encoding="utf-8")

    exit_code = cli_main(_analyze_args(cli_env, "--snapshot", str(snapshot_file)))

    err = capsys.readouterr().err
    assert exit_code == 2
    assert "lineup must contain at least one entry" in err
    assert "Traceback" not in err
    assert not Path(str(cli_env["out"])).exists()


def test_cli_reports_missing_snapshot_file_and_contract_mismatch(
    capsys: pytest.CaptureFixture[str],
    tmp_path: Path,
    cli_env: dict[str, object],
) -> None:
    assert cli_main(_analyze_args(cli_env, "--snapshot", str(tmp_path / "nope.json"))) == 2
    assert "snapshot file not found" in capsys.readouterr().err

    legacy = tmp_path / "legacy.json"
    legacy.write_text(json.dumps({"trackedTeam": {}, "lineup": []}), encoding="utf-8")
    assert cli_main(_analyze_args(cli_env, "--snapshot", str(legacy))) == 2
    assert "analysis-snapshot contract" in capsys.readouterr().err
