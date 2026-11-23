FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1

RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    libgl1 \
    libglib2.0-0 \
    curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY pyproject.toml README.md ./
COPY src ./src
COPY sample_snapshots ./sample_snapshots

# Install core dependencies plus ONNX runtime by default;
# switch INSTALL_EXTRAS to "yolo,onnx" when running local Ultralytics weights in Docker.
ARG INSTALL_EXTRAS=onnx
RUN pip install --upgrade pip && pip install ".[${INSTALL_EXTRAS}]"

EXPOSE 8000

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8000/health || exit 1

CMD ["uvicorn", "video_analyzer.api.app:app", "--host", "0.0.0.0", "--port", "8000"]
