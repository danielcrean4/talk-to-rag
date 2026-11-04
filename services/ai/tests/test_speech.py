import io
from fastapi.testclient import TestClient

from talk_to_rag.api.app import create_app
from talk_to_rag.speech import set_stt, set_tts
from talk_to_rag.speech.adapters.fake import FakeSpeechToText, FakeTextToSpeech


def test_transcribe_audio_endpoint():
    set_stt(FakeSpeechToText(transcript="what is the normal equation"))
    client = TestClient(create_app())

    audio_bytes = b"RIFF....WAVEfmt ...."
    response = client.post(
        "/v1/speech/transcribe",
        content=audio_bytes,
        headers={"Content-Type": "audio/wav"},
    )

    assert response.status_code == 200
    data = response.json()
    assert data["text"] == "what is the normal equation"


def test_synthesize_speech_endpoint():
    set_tts(FakeTextToSpeech())
    client = TestClient(create_app())

    response = client.post(
        "/v1/speech/synthesize",
        json={"text": "Normal equation is X transpose X inverse X transpose y."},
    )

    assert response.status_code == 200
    assert response.headers["content-type"] == "audio/wav"
    assert len(response.content) > 44  # WAV header is 44 bytes


def test_speech_endpoints_in_openapi():
    paths = TestClient(create_app()).get("/openapi.json").json()["paths"]
    assert "/v1/speech/transcribe" in paths
    assert "/v1/speech/synthesize" in paths
