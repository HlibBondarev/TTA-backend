FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PYTHONPATH=/app/src

# Install system dependencies and create a dedicated unprivileged runtime user
# (its home directory hosts library caches/config).
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ffmpeg \
    libgl1 \
    libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system app \
    && useradd --system --gid app --create-home --home-dir /home/app app

WORKDIR /app

# Install only fully pinned, hash-verified wheels. The lock files in requirements/ are generated
# from pyproject.toml; the application itself is imported from /app/src via PYTHONPATH.
# Supported INSTALL_EXTRAS values: "onnx" (default), "yolo", "yolo,onnx" (or "onnx,yolo").
ARG INSTALL_EXTRAS=onnx
COPY requirements ./requirements
RUN extras="$(printf '%s' "${INSTALL_EXTRAS}" | tr -d '[:space:]')"     && case "${extras}" in         onnx) lock_file="requirements/onnx.txt" ;;         yolo) lock_file="requirements/yolo.txt" ;;         yolo,onnx|onnx,yolo) lock_file="requirements/yolo-onnx.txt" ;;         *) echo "Unsupported INSTALL_EXTRAS='${INSTALL_EXTRAS}'. Use one of: onnx, yolo, yolo,onnx" >&2; exit 1 ;;     esac     && pip install --only-binary :all: --require-hashes -r "${lock_file}"

COPY --chown=app:app src ./src

# Keep the documented `tta-video-analyzer` CLI entry point available without installing the package.
RUN { echo '#!/bin/sh'; echo 'exec python -m video_analyzer.cli "$@"'; } \
    > /usr/local/bin/tta-video-analyzer \
    && chmod 755 /usr/local/bin/tta-video-analyzer

COPY --chown=app:app sample_snapshots ./sample_snapshots

USER app

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8000/health || exit 1

CMD ["uvicorn", "video_analyzer.api.app:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000"]