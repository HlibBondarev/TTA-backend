"""Loading the match snapshot from a local JSON file or the .NET ``analysis-snapshot`` endpoint."""

from __future__ import annotations

import json
from pathlib import Path
from urllib.parse import urlparse
from uuid import UUID

import httpx
from pydantic import ValidationError

from video_analyzer.schemas.request import MatchSnapshot

_ALLOWED_SCHEMES = frozenset({"http", "https"})
_SNAPSHOT_PATH_TEMPLATE = "api/matches/{match_id}/teams/{team_id}/analysis-snapshot"
_ERROR_DETAIL_MAX_CHARS = 200


class SnapshotSourceError(Exception):
    """A snapshot could not be loaded; the message is safe to show to a CLI user."""


def load_snapshot_file(path: Path) -> MatchSnapshot:
    """Parse a snapshot JSON file (the saved response of the analysis-snapshot endpoint)."""
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError) as exc:
        raise SnapshotSourceError(f"cannot read snapshot file {path}: {exc}") from exc
    except json.JSONDecodeError as exc:
        raise SnapshotSourceError(f"snapshot file {path} is not valid JSON: {exc}") from exc
    return _validate_snapshot(raw, origin=str(path))


def _normalize_base_url(base_url: str) -> str:
    """Return a canonical ``scheme://host[:port][/prefix]`` base URL or raise.

    Only plain http(s) origins are accepted: no credentials, query, fragment or params, and a
    valid port when one is given.
    """
    try:
        parsed = urlparse(base_url.strip())
        port = parsed.port
    except ValueError as exc:
        raise SnapshotSourceError(f"API base URL is malformed: {exc}") from exc
    if parsed.scheme not in _ALLOWED_SCHEMES or not parsed.hostname:
        raise SnapshotSourceError("API base URL must be an absolute http(s) URL with a host.")
    if parsed.username is not None or parsed.password is not None:
        raise SnapshotSourceError("API base URL must not contain embedded credentials.")
    if parsed.query or parsed.fragment or parsed.params:
        raise SnapshotSourceError("API base URL must not contain a query, params or fragment.")
    host = f"[{parsed.hostname}]" if ":" in parsed.hostname else parsed.hostname
    netloc = host if port is None else f"{host}:{port}"
    return f"{parsed.scheme}://{netloc}{parsed.path.rstrip('/')}"


def _normalize_uuid(value: UUID | str, label: str) -> UUID:
    """Return ``value`` as a canonical UUID; anything else cannot reach the URL path."""
    if isinstance(value, UUID):
        return value
    try:
        return UUID(str(value))
    except (ValueError, AttributeError, TypeError) as exc:
        raise SnapshotSourceError(f"{label} must be a valid UUID.") from exc


def build_snapshot_url(base_url: str, match_id: UUID | str, team_id: UUID | str) -> str:
    """Build the validated analysis-snapshot URL from a base URL and two UUIDs."""
    base = _normalize_base_url(base_url)
    match_uuid = _normalize_uuid(match_id, "match id")
    team_uuid = _normalize_uuid(team_id, "team id")
    path = _SNAPSHOT_PATH_TEMPLATE.format(match_id=match_uuid, team_id=team_uuid)
    return f"{base}/{path}"


def fetch_snapshot(
    base_url: str,
    match_id: UUID | str,
    team_id: UUID | str,
    timeout_sec: float = 30.0,
    client: httpx.Client | None = None,
) -> MatchSnapshot:
    """Fetch and validate ``GET api/matches/{matchId}/teams/{teamId}/analysis-snapshot``.

    Redirects are never followed. Inputs are validated before any request is issued.

    Args:
        client: Optional preconfigured client (used by tests with a mocked transport).

    Raises:
        SnapshotSourceError: On an invalid URL or id, HTTP error status, network failure,
            malformed JSON, or a payload that does not match the snapshot contract.
    """
    url = build_snapshot_url(base_url, match_id, team_id)
    try:
        if client is not None:
            response = client.get(url, timeout=timeout_sec, follow_redirects=False)
        else:
            with httpx.Client(timeout=timeout_sec, follow_redirects=False) as owned_client:
                response = owned_client.get(url)
    except httpx.InvalidURL as exc:
        raise SnapshotSourceError(f"API base URL is invalid: {exc}") from exc
    except httpx.HTTPError as exc:
        raise SnapshotSourceError(f"request to the snapshot API failed: {exc}") from exc

    if response.status_code != httpx.codes.OK:
        raise SnapshotSourceError(_describe_http_error(response))
    try:
        raw = response.json()
    except ValueError as exc:
        raise SnapshotSourceError(
            "snapshot API returned a response that is not valid JSON."
        ) from exc
    return _validate_snapshot(raw, origin="snapshot API response")


def _describe_http_error(response: httpx.Response) -> str:
    detail = response.text.strip()[:_ERROR_DETAIL_MAX_CHARS]
    suffix = f": {detail}" if detail else ""
    return f"snapshot API returned HTTP {response.status_code}{suffix}"


def _validate_snapshot(raw: object, origin: str) -> MatchSnapshot:
    try:
        return MatchSnapshot.model_validate(raw)
    except ValidationError as exc:
        raise SnapshotSourceError(
            f"{origin} does not match the analysis-snapshot contract: "
            f"{_summarize_validation_error(exc)}"
        ) from exc


def _summarize_validation_error(exc: ValidationError) -> str:
    first = exc.errors()[0]
    location = ".".join(str(part) for part in first["loc"]) or "<root>"
    return f"{exc.error_count()} error(s), first at '{location}': {first['msg']}"
