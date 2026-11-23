"""Local CLI runner for testing the hybrid video analysis pipeline and serving the API."""

from __future__ import annotations

import argparse
import json
import logging
import sys
from datetime import datetime
from pathlib import Path
from uuid import uuid4

import uvicorn

from video_analyzer.config import Settings
from video_analyzer.pipeline.factory import build_default_orchestrator
from video_analyzer.schemas.request import (
    AnalysisOptions,
    AnalyzeJobRequest,
    MatchSnapshot,
    VideoSource,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="tta-video-analyzer",
        description="TTA Water Polo hybrid two-pass video analysis CLI.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    analyze_cmd = subparsers.add_parser(
        "analyze",
        help="Analyze a local video file and write the TTA draft JSON output.",
    )
    analyze_cmd.add_argument("--video", required=True, help="Path to local match video file.")
    analyze_cmd.add_argument(
        "--snapshot",
        required=True,
        help=(
            "Path to MatchSnapshot JSON file "
            "(see sample_snapshots/waterpolo_default_snapshot.json)."
        ),
    )
    analyze_cmd.add_argument(
        "--out",
        required=True,
        help="Destination path for the generated TTA draft JSON file.",
    )
    analyze_cmd.add_argument(
        "--video-start-utc",
        default=None,
        help="Optional ISO-8601 UTC start timestamp of the recording (e.g. 2026-11-23T15:00:00Z).",
    )
    analyze_cmd.add_argument(
        "--debug-dir",
        default=None,
        help="Optional directory to save Pass 2 keyframe annotations and cap crop contact sheets.",
    )
    analyze_cmd.add_argument(
        "--skip-pass2",
        action="store_true",
        help="Run Pass 1 VLM detection only (skip Pass 2 tracking and cap recognition).",
    )
    analyze_cmd.add_argument(
        "--generate-presences",
        action="store_true",
        help="Opt in to generating closed playerPresence intervals for visually resolved players.",
    )
    analyze_cmd.add_argument(
        "--env-file",
        default=".env",
        help="Path to .env configuration file (default: .env in current directory).",
    )

    serve_cmd = subparsers.add_parser(
        "serve",
        help="Start the internal FastAPI microservice.",
    )
    serve_cmd.add_argument("--host", default=None, help="Bind host (overrides SERVICE_HOST).")
    serve_cmd.add_argument(
        "--port", type=int, default=None, help="Bind port (overrides SERVICE_PORT)."
    )
    serve_cmd.add_argument("--env-file", default=".env", help="Path to .env configuration file.")

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    env_path = Path(args.env_file) if args.env_file else None
    settings = Settings(_env_file=env_path) if env_path and env_path.is_file() else Settings()

    logging.basicConfig(
        level=getattr(logging, settings.log_level.upper(), logging.INFO),
        format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    )

    if args.command == "serve":
        uvicorn.run(
            "video_analyzer.api.app:app",
            host=args.host or settings.service_host,
            port=args.port or settings.service_port,
        )
        return 0

    if args.command == "analyze":
        return _run_analyze(args, settings)

    parser.print_help()
    return 1


def _run_analyze(args: argparse.Namespace, settings: Settings) -> int:
    video_path = Path(args.video).resolve()
    snapshot_path = Path(args.snapshot).resolve()
    out_path = Path(args.out).resolve()
    debug_dir = Path(args.debug_dir).resolve() if args.debug_dir else None

    if not video_path.is_file():
        print(f"Error: video file not found: {video_path}", file=sys.stderr)
        return 2
    if not snapshot_path.is_file():
        print(f"Error: snapshot file not found: {snapshot_path}", file=sys.stderr)
        return 2

    snapshot_data = json.loads(snapshot_path.read_text(encoding="utf-8"))
    snapshot = MatchSnapshot.model_validate(snapshot_data)

    video_start_utc = (
        datetime.fromisoformat(args.video_start_utc.replace("Z", "+00:00"))
        if args.video_start_utc
        else None
    )

    request = AnalyzeJobRequest(
        job_id=uuid4(),
        match_id=uuid4(),
        tracked_team_id=snapshot.tracked_team.id,
        video_source=VideoSource(kind="local_path", local_path=str(video_path)),
        video_start_utc=video_start_utc,
        match_snapshot=snapshot,
        analysis_options=AnalysisOptions(
            generate_player_presences=args.generate_presences,
            skip_pass2=args.skip_pass2,
        ),
    )

    def on_progress(stage: str, pct: int) -> None:
        print(f"[{pct:3d}%] {stage}")

    orchestrator = build_default_orchestrator(settings)
    draft = orchestrator.run(
        request=request,
        progress_callback=on_progress,
        debug_dir=debug_dir,
    )

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(
        json.dumps(draft.model_dump(mode="json", by_alias=True), indent=2),
        encoding="utf-8",
    )

    print(
        f"Draft saved to {out_path} "
        f"(events={len(draft.events)}, anchors={len(draft.time_anchors)}, "
        f"resolved={draft.diagnostics.resolved_actor_count}, "
        f"unresolved={draft.diagnostics.unresolved_actor_count})."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
