# MeloTTS Local Service

This directory contains the same-host Python wrapper that exposes a narrow HTTP API for reply TTS:

- `GET /health`
- `GET /voices`
- `POST /synthesize`

The service is intended to run on the same machine as `WebCode`. `WebCode` treats it as a local black-box synth service and should not read or write model files directly.

## Storage Layout

Do not default this service to `C:` on Windows. Use a non-system storage root unless an operator explicitly accepts the `C:` override.

Approved layout under the chosen storage root:

```text
<storage-root>/
  cache/
    huggingface/
    pip/
    torch/
    transformers/
  ffmpeg/
    bin/
      ffmpeg.exe        # Windows
      ffmpeg            # non-Windows
  logs/
  models/
  service/
  temp/
  venv/
```

The startup scripts redirect these environment variables under the storage root:

- `HF_HOME`
- `TRANSFORMERS_CACHE`
- `TORCH_HOME`
- `TEMP`
- `TMP`
- `PIP_CACHE_DIR`

## Requirements

- Python with `fastapi`, `uvicorn`, and `melotts`
- `pytest` if you want to run the local verification steps from this README
- `ffmpeg` installed under the approved storage root, for example:
  - Windows: `<storage-root>\ffmpeg\bin\ffmpeg.exe`
  - non-Windows: `<storage-root>/ffmpeg/bin/ffmpeg`

Install the Python dependencies with:

```powershell
python -m pip install -r requirements.txt
```

Install the test dependency for local verification:

```powershell
python -m pip install pytest
```

If `MeloTTS` is not installed or cannot initialize, the service still starts and `GET /health` reports `status = "unavailable"`. Once the runtime is installed correctly, startup prefers GPU and falls back to CPU automatically if GPU initialization fails.

## Windows Startup

Pass an explicit non-empty storage root. The script refuses a `C:` root unless `-AllowSystemDrive` is supplied.

```powershell
cd D:\VSWorkshop\WebCode\tools\melotts-service
.\start.ps1 -StorageRoot D:\WebCodeData\MeloTTS -Port 5057
```

Intentional `C:` override:

```powershell
.\start.ps1 -StorageRoot C:\WebCodeData\MeloTTS -AllowSystemDrive
```

## Non-Windows Startup

```bash
cd /path/to/WebCode/tools/melotts-service
chmod +x start.sh
./start.sh /data/webcode/melotts
```

The non-Windows script also requires a non-empty storage root and binds Uvicorn to `127.0.0.1` by default.

## Manual Smoke Check

Run the service directly:

```powershell
python app.py
```

Then query the local health endpoint:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5057/health
```

Expected health payload fields:

- `status`
- `device`
- `defaultVoiceId`

On a healthy runtime the service reports `status = "ok"` and the actual active device. If GPU startup fails, the service retries on CPU and `/health` reports `device = "cpu"`.
