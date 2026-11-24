# TTA Video Analyzer (Phase 1 MVP)

Isolated Python microservice (`FastAPI` + `FFmpeg` + pluggable object detector + `BoT-SORT` + `Gemini API`) implementing hybrid two-pass post-match video analysis for TTA Water Polo.

## Architecture Overview

1. **Pass 1 — Global Event & Timing Detection (Gemini VLM)**:
   - Downscales the source video to 720p at low FPS while keeping mono AAC audio (whistles, period buzzers) and splits it into overlapping chunks.
   - Prompts Gemini with exact `promptCode -> eventDefinitionId` mappings so duplicate names (e.g. positive `Goal` `G` vs. negative `Goal` `GC`) and custom user definitions never collide.
   - Deduplicates overlap-zone detections and reconciles period/stoppage anchors (`PeriodStart`, `PeriodEnd`, paired `StoppageStart`/`StoppageEnd`).
2. **Pass 2 — Targeted 4K Tracking & Cap Number Resolution**:
   - Extracts narrow native-resolution 4K windows (`±4s`) around each Pass 1 event.
   - Runs a pluggable object detector (`roboflow`, `local_yolo`, or `onnx`) behind `BaseObjectDetector` + `BoT-SORT` tracking with sparse optical-flow camera motion compensation.
   - Selects top-K sharpest native-4K cap crops into a numbered contact sheet and resolves the cap number via Gemini Vision (`gemini_crop_grid`) or optional local OCR (`local_ocr`), constrained by `matchSnapshot.lineup`.
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

Both Pass 1 (`GEMINI_PASS1_MODEL`) and Pass 2 (`GEMINI_PASS2_MODEL`) default to `gemini-4-hydrogen` and can be overridden independently. Each completed draft includes aggregated Gemini token counters (`promptTokens`, `candidatesTokens`, `thoughtsTokens`, `cachedContentTokens`, `totalTokens`) and an estimated USD cost breakdown (`diagnostics.geminiUsage`). You can override or extend per-1M-token USD pricing estimates via `GEMINI_MODEL_PRICING_USD_PER_1M` (JSON object or path to a JSON file).

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

---

## Running Tests

```powershell
cd C:\Users\HlibBondarev\source\repos\TTA-backend\services\video-analyzer
.\.venv\Scripts\Activate.ps1
pytest -v
```
