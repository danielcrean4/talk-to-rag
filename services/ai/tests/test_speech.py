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
    assert "/v1/speech/route-intent" in paths


def test_command_router_fast_path():
    from talk_to_rag.speech.router import LayeredCommandRouter

    router = LayeredCommandRouter()
    assert router.route("next page").intent == "next_page"
    assert router.route("next").route == "fast_path"
    assert router.route("previous page").intent == "prev_page"
    assert router.route("back").intent == "prev_page"
    assert router.route("read this").intent == "read_this"
    assert router.route("summarize this page").intent == "summarize_page"

    pull_up = router.route("pull up normal equation")
    assert pull_up.intent == "pull_up"
    assert pull_up.topic == "normal equation"

    search = router.route("search for convex optimization")
    assert search.intent == "search"
    assert search.query == "convex optimization"


def test_command_router_llm_and_fallback():
    from talk_to_rag.llm.adapters.fake import FakeChat
    from talk_to_rag.speech.router import LayeredCommandRouter, ScreenContext

    class CannedChat(FakeChat):
        def __init__(self, reply: str):
            self._reply = reply

        def complete(self, messages):
            return self._reply

    fake_model = CannedChat(reply='{"intent": "pull_up", "topic": "gradient descent", "query": null}')
    router = LayeredCommandRouter(model=fake_model)
    context = ScreenContext(source_title="cs229-notes1.pdf", current_page=5, active_view="reader")

    result = router.route("could you find the section on gradient descent", context)
    assert result.intent == "pull_up"
    assert result.topic == "gradient descent"
    assert result.route == "llm"

    # Test fallback on invalid JSON from LLM
    broken_model = CannedChat(reply="I am not sure what to do")
    broken_router = LayeredCommandRouter(model=broken_model)
    fallback = broken_router.route("tell me a joke about matrices")
    assert fallback.intent == "search"
    assert fallback.route == "fallback"


def test_route_intent_api_endpoint():
    client = TestClient(create_app())
    response = client.post(
        "/v1/speech/route-intent",
        json={
            "transcript": "pull up normal equation",
            "screen_context": {"current_page": 8, "active_view": "reader"},
        },
    )
    assert response.status_code == 200
    data = response.json()
    assert data["intent"] == "pull_up"
    assert data["topic"] == "normal equation"
    assert data["route"] == "fast_path"
