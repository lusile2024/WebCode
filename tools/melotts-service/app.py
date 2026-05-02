from __future__ import annotations

import logging
import os
import tempfile
import uuid
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any, Callable, Protocol

import uvicorn
from fastapi import FastAPI, HTTPException, Response
from pydantic import BaseModel

try:
    from pydantic import field_validator

    PYDANTIC_V2 = True
except ImportError:  # pragma: no cover - compatibility path
    from pydantic import validator as field_validator

    PYDANTIC_V2 = False


LOGGER = logging.getLogger("melotts-service")

DEFAULT_VOICE_ID = "zh_female_default"
DEFAULT_PREFERRED_DEVICE = "gpu-auto"
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 5057

VOICE_CATALOG = (
    {
        "voiceId": "zh_female_default",
        "displayName": "Chinese Female Default",
        "language": "zh",
        "gender": "female",
        "meloLanguage": "ZH",
        "meloSpeaker": "ZH",
    },
    {
        "voiceId": "en_default",
        "displayName": "English Default",
        "language": "en",
        "gender": "unknown",
        "meloLanguage": "EN",
        "meloSpeaker": "EN-Default",
    },
    {
        "voiceId": "en_us",
        "displayName": "English US",
        "language": "en",
        "gender": "unknown",
        "meloLanguage": "EN",
        "meloSpeaker": "EN-US",
    },
    {
        "voiceId": "en_br",
        "displayName": "English BR",
        "language": "en",
        "gender": "unknown",
        "meloLanguage": "EN",
        "meloSpeaker": "EN-BR",
    },
    {
        "voiceId": "en_india",
        "displayName": "English India",
        "language": "en",
        "gender": "unknown",
        "meloLanguage": "EN",
        "meloSpeaker": "EN_INDIA",
    },
    {
        "voiceId": "en_au",
        "displayName": "English AU",
        "language": "en",
        "gender": "unknown",
        "meloLanguage": "EN",
        "meloSpeaker": "EN-AU",
    },
    {
        "voiceId": "es_default",
        "displayName": "Spanish Default",
        "language": "es",
        "gender": "unknown",
        "meloLanguage": "ES",
        "meloSpeaker": "ES",
    },
    {
        "voiceId": "fr_default",
        "displayName": "French Default",
        "language": "fr",
        "gender": "unknown",
        "meloLanguage": "FR",
        "meloSpeaker": "FR",
    },
    {
        "voiceId": "jp_default",
        "displayName": "Japanese Default",
        "language": "ja",
        "gender": "unknown",
        "meloLanguage": "JP",
        "meloSpeaker": "JP",
    },
    {
        "voiceId": "kr_default",
        "displayName": "Korean Default",
        "language": "ko",
        "gender": "unknown",
        "meloLanguage": "KR",
        "meloSpeaker": "KR",
    },
)


class EngineAdapter(Protocol):
    device: str

    def list_voices(self) -> list[dict[str, str]]:
        ...

    def synthesize(self, text: str, voice_id: str) -> bytes:
        ...


class EngineRuntime:
    def __init__(
        self,
        *,
        adapter: EngineAdapter,
        status: str,
        device: str,
        error: str | None = None,
    ):
        self.adapter = adapter
        self.status = status
        self.device = device
        self.error = error


class SynthesizeRequest(BaseModel):
    text: str
    voice_id: str

    @field_validator("text")
    @classmethod
    def validate_text(cls, value: str) -> str:
        if not value or not value.strip():
            raise ValueError("text must not be blank")

        return value.strip()

    @field_validator("voice_id")
    @classmethod
    def validate_voice_id(cls, value: str) -> str:
        if not value or not value.strip():
            raise ValueError("voice_id must not be blank")

        return value.strip()


class UnavailableEngineAdapter:
    def __init__(self, reason: str):
        self.device = "unavailable"
        self.reason = reason

    def list_voices(self) -> list[dict[str, str]]:
        return []

    def synthesize(self, text: str, voice_id: str) -> bytes:
        raise RuntimeError(self.reason)


class MeloEngineAdapter:
    def __init__(self, device: str):
        try:
            from melo.api import TTS
        except Exception as exc:  # pragma: no cover - depends on local runtime
            raise RuntimeError(
                "MeloTTS runtime is not installed. Install the 'melotts' package "
                "and its model dependencies before starting the service."
            ) from exc

        self.device = device
        self._tts_class = TTS
        self._models: dict[str, Any] = {}
        self._voices: dict[str, dict[str, str]] = {}

        load_errors: list[str] = []
        for spec in VOICE_CATALOG:
            language = spec["meloLanguage"]
            model = self._models.get(language)
            if model is None:
                try:
                    model = self._tts_class(language=language, device=device)
                except Exception as exc:  # pragma: no cover - depends on local runtime
                    load_errors.append(f"{language}: {exc}")
                    continue

                self._models[language] = model

            speaker_ids = getattr(getattr(model, "hps", None), "data", None)
            spk2id = getattr(speaker_ids, "spk2id", {})
            if spec["meloSpeaker"] not in spk2id:
                continue

            self._voices[spec["voiceId"]] = normalize_voice(
                {
                    "voiceId": spec["voiceId"],
                    "displayName": spec["displayName"],
                    "language": spec["language"],
                    "gender": spec["gender"],
                }
            )

        if not self._voices:
            message = "No MeloTTS voices were discovered."
            if load_errors:
                message = f"{message} Loader errors: {'; '.join(load_errors)}"
            raise RuntimeError(message)

    def list_voices(self) -> list[dict[str, str]]:
        return list(self._voices.values())

    def synthesize(self, text: str, voice_id: str) -> bytes:
        spec = next((item for item in VOICE_CATALOG if item["voiceId"] == voice_id), None)
        if spec is None or voice_id not in self._voices:
            raise ValueError(f"Unknown voice_id: {voice_id}")

        model = self._models[spec["meloLanguage"]]
        speaker_ids = getattr(getattr(model, "hps", None), "data", None)
        spk2id = getattr(speaker_ids, "spk2id", {})
        speaker_id = spk2id.get(spec["meloSpeaker"])
        if speaker_id is None:
            raise ValueError(f"Voice is not available at runtime: {voice_id}")

        output_path = Path(tempfile.gettempdir()) / f"melotts-{uuid.uuid4().hex}.wav"
        try:
            model.tts_to_file(text, speaker_id, str(output_path), speed=1.0)
            return output_path.read_bytes()
        finally:
            output_path.unlink(missing_ok=True)


def normalize_voice(raw_voice: dict[str, Any]) -> dict[str, str]:
    voice_id = (
        raw_voice.get("voiceId")
        or raw_voice.get("voice_id")
        or raw_voice.get("id")
        or ""
    )
    display_name = (
        raw_voice.get("displayName")
        or raw_voice.get("display_name")
        or raw_voice.get("name")
        or voice_id
    )
    language = raw_voice.get("language") or "unknown"
    gender = raw_voice.get("gender") or "unknown"

    return {
        "voiceId": str(voice_id),
        "displayName": str(display_name),
        "language": str(language),
        "gender": str(gender),
    }


def default_adapter_factory(device: str) -> EngineAdapter:
    return MeloEngineAdapter(device)


def build_device_candidates(preferred_device: str | None) -> list[str]:
    preferred = (preferred_device or DEFAULT_PREFERRED_DEVICE).strip()
    preferred_lower = preferred.lower()

    if preferred_lower in {"", "gpu-auto", "auto", "gpu", "cuda"}:
        candidates = ["cuda", "cpu"]
    elif preferred_lower.startswith("cuda"):
        candidates = [preferred, "cpu"]
    elif preferred_lower == "cpu":
        candidates = ["cpu"]
    else:
        candidates = [preferred, "cpu"]

    unique_candidates: list[str] = []
    for candidate in candidates:
        if candidate not in unique_candidates:
            unique_candidates.append(candidate)

    return unique_candidates


def initialize_runtime(
    adapter_factory: Callable[[str], EngineAdapter],
    preferred_device: str | None,
) -> EngineRuntime:
    last_error: Exception | None = None

    for candidate in build_device_candidates(preferred_device):
        try:
            adapter = adapter_factory(candidate)
            LOGGER.info("Initialized MeloTTS adapter on %s", adapter.device)
            return EngineRuntime(adapter=adapter, status="ok", device=adapter.device)
        except Exception as exc:
            last_error = exc
            LOGGER.warning("Failed to initialize MeloTTS on %s: %s", candidate, exc)

    message = "Unable to initialize MeloTTS engine."
    if last_error is not None:
        message = f"{message} {last_error}"

    LOGGER.error(message)
    return EngineRuntime(
        adapter=UnavailableEngineAdapter(message),
        status="unavailable",
        device="unavailable",
        error=message,
    )


def create_app(
    *,
    adapter_factory: Callable[[str], EngineAdapter] | None = None,
    default_voice_id: str | None = None,
    preferred_device: str | None = None,
) -> FastAPI:
    adapter_factory = adapter_factory or default_adapter_factory
    default_voice_id = default_voice_id or os.getenv(
        "MELOTTS_DEFAULT_VOICE_ID", DEFAULT_VOICE_ID
    )
    preferred_device = preferred_device or os.getenv(
        "MELOTTS_PREFERRED_DEVICE", DEFAULT_PREFERRED_DEVICE
    )

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        app.state.runtime = initialize_runtime(adapter_factory, preferred_device)
        yield

    app = FastAPI(title="MeloTTS Local Service", version="0.1.0", lifespan=lifespan)
    app.state.default_voice_id = default_voice_id

    @app.get("/health")
    async def health() -> dict[str, str]:
        runtime = app.state.runtime
        response = {
            "status": runtime.status,
            "device": runtime.device,
            "defaultVoiceId": app.state.default_voice_id,
        }
        if runtime.error:
            response["message"] = runtime.error

        return response

    @app.get("/voices")
    async def voices() -> dict[str, list[dict[str, str]]]:
        runtime = app.state.runtime
        if runtime.status != "ok":
            raise HTTPException(status_code=503, detail=runtime.error or "TTS unavailable")

        return {"voices": [normalize_voice(item) for item in runtime.adapter.list_voices()]}

    @app.post("/synthesize")
    async def synthesize(request: SynthesizeRequest) -> Response:
        runtime = app.state.runtime
        if runtime.status != "ok":
            raise HTTPException(status_code=503, detail=runtime.error or "TTS unavailable")

        try:
            audio = runtime.adapter.synthesize(request.text, request.voice_id)
        except ValueError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
        except RuntimeError as exc:
            raise HTTPException(status_code=503, detail=str(exc)) from exc

        return Response(content=audio, media_type="audio/wav")

    return app


app = create_app()


if __name__ == "__main__":
    logging.basicConfig(level=os.getenv("MELOTTS_LOG_LEVEL", "INFO").upper())
    uvicorn.run(
        app,
        host=os.getenv("MELOTTS_HOST", DEFAULT_HOST),
        port=int(os.getenv("MELOTTS_PORT", str(DEFAULT_PORT))),
        log_level=os.getenv("MELOTTS_LOG_LEVEL", "info"),
    )
