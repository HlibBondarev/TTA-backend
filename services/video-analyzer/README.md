# TTA Video Analyzer (Phase 1 MVP)

Isolated Python microservice (`FastAPI` + `FFmpeg` + pluggable object detector + `BoT-SORT` + `Gemini API`) implementing hybrid two-pass post-match video analysis for TTA Water Polo.

## Architecture Overview

1. **Pass 1 — Global Event & Timing Detection (Gemini VLM)**:
   - Transcodes the source **once** into a 720p H.264 mezzanine (hardware-accelerated when available, mono 16 kHz AAC audio kept for whistles and period buzzers) with a forced keyframe interval (1-5 s) that evenly divides the chunk duration, the overlap and the chunk step. Overlapping chunks are then cut from the mezzanine with `-c copy`, so the 4K source is never re-decoded per chunk.
   - Analyzes chunks in parallel (`PASS1_MAX_CONCURRENCY`) and merges results by chunk index, so output order is deterministic.
   - Prompts Gemini with exact `promptCode -> eventDefinitionId` mappings so duplicate names (e.g. positive `Goal` `G` vs. negative `Goal` `GC`) and custom user definitions never collide.
   - Deduplicates overlap-zone detections and reconciles period/stoppage anchors (`PeriodStart`, `PeriodEnd`, paired `StoppageStart`/`StoppageEnd`).
2. **Pass 2 — Targeted 4K Tracking & Cap Number Resolution**:
   - **Phase A** (`PASS2_PHASE_A_MAX_CONCURRENCY`): for every Pass 1 event, streams a narrow window (`±2.5 s` at `4 fps`, about 20 frames) through an ffmpeg `rawvideo` pipe (no temp JPEGs), runs a pluggable object detector (`roboflow`, `local_yolo`, or `onnx`) on frames downscaled to `DETECTOR_INPUT_WIDTH` (boxes are rescaled to native pixels), tracks players with `BoT-SORT` + sparse optical-flow camera motion compensation, and selects the top-K sharpest **native-resolution** cap crops. Native frames are kept only as compact in-memory JPEG copies and decoded on demand for cap crops. Per-frame `BALL` and `GOAL` detections are preserved in the tracker output. Remote detector calls share one global `DETECTOR_MAX_CONCURRENCY` semaphore across all Phase A tasks; local providers run serially.
   - **Phase B** (`PASS2_PHASE_B_MAX_CONCURRENCY`): resolves the cap number from the prepared items via Gemini Vision (`gemini_crop_grid`) or optional local OCR (`local_ocr`), constrained by `matchSnapshot.lineup`. If the Gemini circuit breaker opens, Phase A results are kept: remaining events use the local OCR fallback when `easyocr` is installed, otherwise they are marked with an explicit review reason.
   - Leaves `matchLineupId = null` with ranked `candidateLineupIds` whenever confidence is below threshold (`minCapConfidence`) or duplicate jersey numbers exist.

---

## Local Windows Setup (Python 3.12 + PowerShell)

### Prerequisites
- **Python 3.12** (installed at `%LOCALAPPDATA%\Programs\Python\Python312\python.exe`)
- **FFmpeg & FFprobe** on `PATH` (or set `FFMPEG_BIN` and `FFPROBE_BIN` in `.env`)
- `GEMINI_API_KEY` and `ROBOFLOW_API_KEY` (when using the default `roboflow` provider)

### Installation

```powershell
cd C:\Users\HlibBondarev\source\repos\TTA-backend\services\video-analyzer

# Create and activate virtual environment using Python 3.12
& "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe" -m venv .venv
.\.venv\Scripts\Activate.ps1

# Install core package + dev test dependencies
python -m pip install --upgrade pip
pip install -e ".[dev]"

# Optional extras depending on which local providers you want to switch to:
# pip install -e ".[yolo]"   # For DETECTOR_PROVIDER=local_yolo (ultralytics)
# pip install -e ".[onnx]"   # For DETECTOR_PROVIDER=onnx (onnxruntime)
# pip install -e ".[ocr]"    # For CAP_RESOLVER_PROVIDER=local_ocr (easyocr)
```

### Configure `.env`

```powershell
Copy-Item .env.example .env
# Edit .env and set a unique INTERNAL_API_KEY (required for API server mode),
# GEMINI_API_KEY, and ROBOFLOW_API_KEY
```

Three Gemini model slots are configured independently: `GEMINI_PASS1_MODEL` (default `gemini-4-hydrogen`), `GEMINI_VERIFIER_MODEL` (default `gemini-4-hydrogen`, reserved for the Pass 1 verifier and unused until #158) and `GEMINI_PASS2_MODEL` (default `gemini-3.5-flash-lite`). Each completed draft includes aggregated Gemini token counters (`promptTokens`, `candidatesTokens`, `thoughtsTokens`, `cachedContentTokens`, `totalTokens`) and an estimated USD cost breakdown (`diagnostics.geminiUsage`). You can override or extend per-1M-token USD pricing estimates via `GEMINI_MODEL_PRICING_USD_PER_1M` (JSON object or path to a JSON file).

### Run CLI Analysis on a Local 4K Match Video

```powershell
tta-video-analyzer analyze `
  --video "C:\Users\HlibBondarev\TTA\TTA-tests\4 min(4k).mov" `
  --snapshot ".\sample_snapshots\waterpolo_default_snapshot.json" `
  --out ".\output\4min_draft.json" `
  --debug-dir ".\debug\4min"
```

Useful optional flags:
- `--video-start-utc "2026-11-23T15:00:00Z"`: Align timestamps to a known recording start UTC time. If omitted, the pipeline automatically selects a safe past `T0` so `T0 + duration <= UtcNow`.
- `--skip-pass2`: Quickly validate Pass 1 Gemini event detection and time anchors only.
- `--generate-presences`: Opt in to emitting closed (`timeOut > timeIn`) player presence intervals for resolved players (disabled by default: `playerPresences: []`).

### Start the Internal FastAPI Server

Configure a unique, non-placeholder `INTERNAL_API_KEY` in `.env` (or environment variables) before starting the server. Local runs bind strictly to `127.0.0.1` by default:

```powershell
tta-video-analyzer serve --host 127.0.0.1 --port 8000
```

If remote access is needed over a trusted private network, configure a strong unique `INTERNAL_API_KEY` first (plus `CALLBACK_ALLOWED_ORIGINS` and `CALLBACK_API_KEY` when using webhook callbacks) before binding `--host 0.0.0.0`.

Verify health:

```powershell
Invoke-RestMethod http://localhost:8000/health
```

---

## Performance, Resilience & Gemini Configuration

All values are environment variables (see `.env.example`). Defaults target a single 60-75 minute 4K match on **Gemini Paid Tier 1**; the last column lists the suggested **Free Tier** values.

| Variable | Default | Free Tier note |
|---|---|---|
| `GEMINI_PASS1_MODEL` / `GEMINI_VERIFIER_MODEL` / `GEMINI_PASS2_MODEL` | `gemini-4-hydrogen` / `gemini-4-hydrogen` / `gemini-3.5-flash-lite` | same |
| `GEMINI_PASS1_THINKING_LEVEL` | `LOW` | unset if the model has no thinking field |
| `GEMINI_VERIFIER_THINKING_LEVEL`, `GEMINI_PASS2_THINKING_LEVEL` | unset | same |
| `GEMINI_PASS1_RPM` / `GEMINI_PASS2_RPM` | `60` / `120` | `5` / `15` |
| `GEMINI_MAX_RETRIES`, `GEMINI_RETRY_BASE_SEC` | `3`, `2.0` | same |
| `GEMINI_MAX_CALLS_PER_JOB` | `120` | `20` |
| `GEMINI_MAX_ESTIMATED_COST_USD` | `3.00` | `0.50` (or empty = unlimited) |
| `PASS1_MAX_CONCURRENCY` | `3` | `1` |
| `PASS2_PHASE_A_MAX_CONCURRENCY` / `PASS2_PHASE_B_MAX_CONCURRENCY` | `4` / `4` | `1` / `1` |
| `PASS2_BATCH_SIZE` (events per Phase A -> Phase B batch) | `48` | `48` |
| `DETECTOR_MAX_CONCURRENCY` (global semaphore across all Phase A tasks) | `4` | `1` |
| `DETECTOR_INPUT_WIDTH` | `1280` | same |
| `ROBOFLOW_MAX_RETRIES`, `ROBOFLOW_RETRY_BASE_SEC` | `3`, `1.0` | same |
| `FFMPEG_HWACCEL`, `FFMPEG_VIDEO_ENCODER` (Pass 1 mezzanine) | `auto`, `auto` | `none` for CI |
| `FFMPEG_EXTRACTION_MAX_CONCURRENCY` (simultaneous 4K Pass 2 decoders) | `2` | `1` |
| `FFMPEG_EXTRACTION_THREADS` (`-threads` per Pass 2 decoder, `0` = ffmpeg default) | `2` | same |
| `FFMPEG_EXTRACTION_HWACCEL` (Pass 2 hardware decode) | `none` | `none` |

**Thinking control.** A thinking level (`MINIMAL`, `LOW`, `MEDIUM`, `HIGH`) is sent per slot as `thinking_level`. When a level is unset the whole `thinking_config` is omitted from the request (some models, such as `gemini-3.5-flash-lite`, reject the field with HTTP 400). `thinking_budget` is never sent and `include_thoughts` is never enabled. Automatic function calling is disabled on every `generate_content` call.

**Encoder selection.** With `FFMPEG_VIDEO_ENCODER=auto` the mezzanine encoder candidates come from `ffmpeg -encoders` in the order `h264_nvenc`, `h264_qsv`, `h264_amf`, `libx264`. An encoder that fails at runtime (a listed encoder may have no matching GPU) is dropped and the **next** candidate is tried, ending with `libx264`; failed encoders are not retried for the life of the process.

**Pass 1 transcode speed.** The mezzanine filter is `fps=...,scale=...` (decimate first, then downscale): on a 4K 30 fps HEVC clip, scaling every frame before dropping ~93% of them was about 4x slower. At 2 fps the encoder is never the bottleneck, so the cost is dominated by decoding the 4K source; on the 4-minute Clip 1 the mezzanine takes about 60 s on a laptop with only an Intel iGPU (QSV is picked after NVENC fails).

**Pass 2 memory.** Each concurrent 4K decoder holds roughly 1-1.7 GB with default threading and hardware surface pools, so they are throttled by their own semaphore (`FFMPEG_EXTRACTION_MAX_CONCURRENCY`) rather than by `PASS2_PHASE_A_MAX_CONCURRENCY`, which can stay higher to overlap remote detector latency. With the defaults (2 decoders, 2 threads each, software decode) the 14-window Clip 1 benchmark peaks at about 1.2 GB for Python plus all ffmpeg processes (previously about 7 GB) with the same wall-clock time. Raw frames are read one at a time into a single reusable buffer, downscaled immediately, and only compact JPEG copies of native frames are kept until Phase A for that event finishes. Events are processed in batches of `PASS2_BATCH_SIZE`: Phase A and Phase B run per batch and each cap-crop bundle (crops plus contact sheet) is released as soon as its cap number is resolved, so peak crop memory is bounded by the batch size instead of the match length.

**Error handling (shared by Pass 1 and Pass 2).**
- Transient failures (HTTP 500/502/503/504, timeouts) and short per-minute `429`s (`retryDelay <= 60 s`, honoring the server delay) are retried with jittered exponential backoff.
- HTTP 400/401/403, daily (`PerDay`) `429 RESOURCE_EXHAUSTED`, or a `retryDelay > 60 s` get **zero retries** and open that slot's circuit breaker; later calls fail fast without touching the API.
- `GEMINI_MAX_CALLS_PER_JOB` and `GEMINI_MAX_ESTIMATED_COST_USD` are enforced per job across all slots (every attempt counts; cost is estimated from `usage_metadata` and the pricing table, and unpriced models never trip the cost limit).
- Each RPM pacer is per model and process-wide; breaker state and budget are per job.

**Pass 2 failure reasons.** Events without a cap reading carry one explicit entry in `reviewReasons` instead of a blanket `NO_PLAYER_TRACKLET_MATCHED`:

| Reason | Meaning |
|---|---|
| `NO_FRAMES_EXTRACTED` | ffmpeg returned no frames for the event window |
| `NO_PLAYER_TRACKLET_MATCHED` | no tracked player matched the Pass 1 actor hint |
| `NO_USABLE_CAP_CROPS` | the matched player had no usable cap crop |
| `CAP_RESOLUTION_API_ERROR` | the cap resolver call or response failed |
| `CAP_RESOLUTION_QUOTA_EXHAUSTED` | the Gemini quota breaker was open |
| `CAP_RESOLUTION_BUDGET_EXCEEDED` | the per-job call/cost budget was reached |

**Timers.** `diagnostics.timingsSec` keeps all earlier keys and adds `pass1Mezzanine`, `pass1ChunkSlicing`, `pass2FrameExtraction`, `pass2DetectorInference`, `pass2Tracking`, `pass2CropSelection` and `pass2CapOcr`. Pass 2 sub-stage values are cumulative seconds summed across events, so with parallel tasks they can exceed wall-clock time. `pass2DetectorInference` includes time queued on the global detector semaphore.

---

## Offline Evaluation Harness

Score a generated draft against coach-provided ground truth without any network or paid API calls:

```powershell
tta-video-analyzer evaluate --draft .\output\clip1_draft.json --labels .\eval_labels\clip1.json --tolerance-sec 3
```

Metrics (events are matched one-to-one by time within `--tolerance-sec`, closest pair first):
- **Event precision / recall**: matched events / predicted events and / labeled events.
- **Label accuracy**: matched pairs with the same `promptCode` / matched pairs.
- **Anchor accuracy**: labeled anchors matched by type (and period when both are known) within the tolerance / labeled anchors.
- **Cap resolution accuracy**: matched pairs whose `resolvedCapNumber` equals the label's `capNumber` / matched pairs with a labeled `capNumber` (an unresolved cap counts as wrong).

A ratio with a zero denominator is shown as `n/a`.

**Label file schema** (`GroundTruthLabels`, camelCase JSON): `clipId` (required), optional `description`, `events[]` (`timeSec`, `promptCode`, optional `periodNumber`, `capNumber`, `notes`) and `anchors[]` (`timeSec`, `type` = `PeriodStart | PeriodEnd | StoppageStart | StoppageEnd`, optional `periodNumber`). `eval_labels/sample_labels.example.json` is a documented template with placeholder values.

**Dev / holdout workflow.**
1. Copy the template to `eval_labels/clip1.json` (dev clip) and `eval_labels/clip2.json` (unseen holdout clip) and fill in the coach's labels. `eval_labels/*` is git-ignored except the example, so real labels never enter the repository.
2. Run `analyze` on each clip, then `evaluate` on each draft.
3. Tune prompts, thinking levels and thresholds against the **dev** clip only; use the **holdout** clip to confirm the changes generalize and to catch overfitting.

---

## Zero-Code Provider & Model Switching

Change `.env` or environment variables without modifying any Python code:

### Switch to Local Ultralytics YOLO (`yolo11m.pt`)
```env
DETECTOR_PROVIDER=local_yolo
DETECTOR_MODEL_PATH_OR_ID=yolo11m.pt
DETECTOR_CLASS_MAP={"person":"player","sports ball":"ball"}
```

### Switch to Local ONNX Runtime Model
```env
DETECTOR_PROVIDER=onnx
DETECTOR_MODEL_PATH_OR_ID=C:\models\waterpolo-yolo11m.onnx
DETECTOR_CLASS_MAP={"0":"player","1":"ball","2":"goal"}
```

### Switch Cap Resolver to Local OCR Fallback
```env
CAP_RESOLVER_PROVIDER=local_ocr
```

---

## Docker Usage

```powershell
cd C:\Users\HlibBondarev\source\repos\TTA-backend\services\video-analyzer
docker build -t tta-video-analyzer:dev .

# Run CLI against a mounted Windows folder
docker run --rm --env-file .env `
  -v "C:\Users\HlibBondarev\TTA\TTA-tests:/videos:ro" `
  -v "${PWD}\output:/output" `
  -v "${PWD}\debug:/debug" `
  tta-video-analyzer:dev `
  tta-video-analyzer analyze `
    --video "/videos/4 min(4k).mov" `
    --snapshot "/app/sample_snapshots/waterpolo_default_snapshot.json" `
    --out "/output/4min_draft.json" `
    --debug-dir "/debug/4min"
```

The image runs as an unprivileged `app` user and installs only hash-pinned wheels from
`requirements/<extras>.txt` (`onnx` by default). `--build-arg INSTALL_EXTRAS=yolo,onnx` (or
`onnx,yolo`) selects `requirements/yolo-onnx.txt` and `--build-arg INSTALL_EXTRAS=yolo` selects
`requirements/yolo.txt`; any other value fails the build. Regenerate the lock files after changing
`pyproject.toml`:

```powershell
uv pip compile pyproject.toml --extra onnx --python-version 3.12 --python-platform linux `
  --generate-hashes --only-binary :all: --no-header -o requirements/onnx.txt
uv pip compile pyproject.toml --extra yolo --python-version 3.12 --python-platform linux `
  --generate-hashes --only-binary :all: --no-header -o requirements/yolo.txt
uv pip compile pyproject.toml --extra yolo --extra onnx --python-version 3.12 --python-platform linux `
  --generate-hashes --only-binary :all: --no-header -o requirements/yolo-onnx.txt
```

**Linux hosts:** the container user has UID/GID 10001 (not root), so bind-mounted output folders must be
writable by it. Create them before `docker run`:

```bash
mkdir -p output debug && sudo chown 10001:10001 output debug
```

---

## Running Tests

```powershell
cd C:\Users\HlibBondarev\source\repos\TTA-backend\services\video-analyzer
.\.venv\Scripts\Activate.ps1
pytest -v
ruff check
ruff format --check
```

All automated tests are fully offline: Gemini, Roboflow and ffmpeg are replaced with mocks and fakes, so no API keys or network access are needed.