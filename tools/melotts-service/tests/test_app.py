from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest
from fastapi.testclient import TestClient


APP_PATH = Path(__file__).resolve().parent.parent / "app.py"


def load_app_module():
    spec = importlib.util.spec_from_file_location("melotts_service_app", APP_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load app module from {APP_PATH}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeEngineAdapter:
    def __init__(self, device: str, voices: list[dict[str, str]], audio: bytes = b"RIFFfake"):
        self.device = device
        self.voices = voices
        self.audio = audio
        self.calls: list[tuple[str, str]] = []

    def list_voices(self):
        return list(self.voices)

    def synthesize(self, text: str, voice_id: str) -> bytes:
        self.calls.append((text, voice_id))
        return self.audio


def test_health_reports_active_device_and_default_voice():
    app_module = load_app_module()
    engine = FakeEngineAdapter(
        device="cuda",
        voices=[
            {
                "id": "zh_female_default",
                "name": "Chinese Female",
                "language": "zh",
                "gender": "female",
            }
        ],
    )

    app = app_module.create_app(
        adapter_factory=lambda device: engine,
        default_voice_id="zh_female_default",
    )

    with TestClient(app) as client:
        response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "device": "cuda",
        "defaultVoiceId": "zh_female_default",
    }


def test_voices_returns_normalized_voice_list():
    app_module = load_app_module()
    engine = FakeEngineAdapter(
        device="cpu",
        voices=[
            {
                "id": "zh_female_default",
                "name": "Chinese Female",
                "language": "zh",
                "gender": "female",
            },
            {
                "id": "en_male_demo",
                "name": "English Male",
                "language": "en",
                "gender": "male",
            },
        ],
    )

    app = app_module.create_app(
        adapter_factory=lambda device: engine,
        default_voice_id="zh_female_default",
    )

    with TestClient(app) as client:
        response = client.get("/voices")

    assert response.status_code == 200
    assert response.json() == {
        "voices": [
            {
                "voiceId": "zh_female_default",
                "displayName": "Chinese Female",
                "language": "zh",
                "gender": "female",
            },
            {
                "voiceId": "en_male_demo",
                "displayName": "English Male",
                "language": "en",
                "gender": "male",
            },
        ]
    }


def test_synthesize_rejects_blank_input():
    app_module = load_app_module()
    engine = FakeEngineAdapter(
        device="cpu",
        voices=[
            {
                "id": "zh_female_default",
                "name": "Chinese Female",
                "language": "zh",
                "gender": "female",
            }
        ],
    )

    app = app_module.create_app(
        adapter_factory=lambda device: engine,
        default_voice_id="zh_female_default",
    )

    with TestClient(app) as client:
        response = client.post(
            "/synthesize",
            json={"text": "   ", "voice_id": "zh_female_default"},
        )

    assert response.status_code == 422


def test_health_reports_cpu_after_gpu_initialization_fallback():
    app_module = load_app_module()
    attempts: list[str] = []

    def factory(device: str):
        attempts.append(device)
        if device == "cuda":
            raise RuntimeError("CUDA init failed")

        return FakeEngineAdapter(
            device="cpu",
            voices=[
                {
                    "id": "zh_female_default",
                    "name": "Chinese Female",
                    "language": "zh",
                    "gender": "female",
                }
            ],
        )

    app = app_module.create_app(
        adapter_factory=factory,
        default_voice_id="zh_female_default",
    )

    with TestClient(app) as client:
        response = client.get("/health")

    assert attempts == ["cuda", "cpu"]
    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "device": "cpu",
        "defaultVoiceId": "zh_female_default",
    }
