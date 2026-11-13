import io
import wave

import pytest
from fastapi.testclient import TestClient

from talk_to_rag.api.app import create_app
from talk_to_rag.speech import SpeechNotConfigured, get_stt, get_tts, set_stt, set_tts
from talk_to_rag.speech.adapters.fake import FakeSpeechToText, FakeTextToSpeech
from talk_to_rag.speech.adapters.kokoro import read_unknown, token_chunks
from talk_to_rag.speech.text import split_sentences, spoken_text
from talk_to_rag.storage import blob_storage


@pytest.fixture(autouse=True)
def isolated_speech(tmp_path, monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_BLOB_ROOT", str(tmp_path))
    monkeypatch.delenv("TALK_TO_RAG_STT", raising=False)
    monkeypatch.delenv("TALK_TO_RAG_TTS", raising=False)
    set_stt(None)
    set_tts(None)
    yield
    set_stt(None)
    set_tts(None)


def test_get_stt_refuses_to_guess_when_nothing_is_selected():
    with pytest.raises(SpeechNotConfigured):
        get_stt()


def test_get_stt_selects_the_fake_by_name(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_STT", "fake")
    assert isinstance(get_stt(), FakeSpeechToText)


def test_transcribe_reads_the_blob_and_returns_the_adapters_text():
    set_stt(FakeSpeechToText("what is the normal equation"))
    blob_storage().write_bytes("speech/question.webm", b"audio-bytes")

    response = TestClient(create_app()).post("/v1/speech/transcribe", json={"blob_key": "speech/question.webm"})

    assert response.status_code == 200
    assert response.json() == {"text": "what is the normal equation"}


def test_transcribe_returns_404_for_a_missing_blob():
    set_stt(FakeSpeechToText())

    response = TestClient(create_app()).post("/v1/speech/transcribe", json={"blob_key": "speech/missing.webm"})

    assert response.status_code == 404


def test_transcribe_returns_400_for_empty_audio():
    set_stt(FakeSpeechToText())
    blob_storage().write_bytes("speech/empty.webm", b"")

    response = TestClient(create_app()).post("/v1/speech/transcribe", json={"blob_key": "speech/empty.webm"})

    assert response.status_code == 400


def test_transcribe_returns_503_when_speech_is_not_configured():
    blob_storage().write_bytes("speech/question.webm", b"audio-bytes")

    response = TestClient(create_app()).post("/v1/speech/transcribe", json={"blob_key": "speech/question.webm"})

    assert response.status_code == 503
    assert "TALK_TO_RAG_STT" in response.json()["detail"]


def test_get_tts_refuses_to_guess_when_nothing_is_selected():
    with pytest.raises(SpeechNotConfigured):
        get_tts()


def test_get_tts_selects_the_fake_by_name(monkeypatch):
    monkeypatch.setenv("TALK_TO_RAG_TTS", "fake")
    assert isinstance(get_tts(), FakeTextToSpeech)


def test_synthesize_writes_a_wav_under_the_requested_key():
    set_tts(FakeTextToSpeech())

    response = TestClient(create_app()).post(
        "/v1/speech/synthesize",
        json={"text": "Gradient descent follows the slope. It stops at a minimum.", "blob_key": "audio/answer.wav"},
    )

    assert response.status_code == 200
    assert response.json() == {
        "blob_key": "audio/answer.wav",
        "format": "wav",
        "content_type": "audio/wav",
        "duration_seconds": 0.2,
    }
    with wave.open(io.BytesIO(blob_storage().read_bytes("audio/answer.wav"))) as wav:
        assert wav.getnchannels() == 1
        assert wav.getnframes() / wav.getframerate() == pytest.approx(0.2)


def test_synthesize_picks_an_audio_key_when_none_is_given():
    set_tts(FakeTextToSpeech())

    response = TestClient(create_app()).post("/v1/speech/synthesize", json={"text": "Hello."})

    key = response.json()["blob_key"]
    assert key.startswith("audio/") and key.endswith(".wav")
    assert blob_storage().exists(key)


def test_synthesize_does_not_read_out_citations_or_markdown():
    tts = FakeTextToSpeech()
    set_tts(tts)

    TestClient(create_app()).post(
        "/v1/speech/synthesize", json={"text": "**Ridge regression** shrinks weights [1]. It adds a penalty [2, 3]."}
    )

    assert tts.spoken == ["Ridge regression shrinks weights.", "It adds a penalty."]


def test_synthesize_passes_the_voice_and_rejects_unknown_ones():
    set_tts(FakeTextToSpeech())
    client = TestClient(create_app())

    assert client.post("/v1/speech/synthesize", json={"text": "Hi.", "voice": "fake"}).status_code == 200
    response = client.post("/v1/speech/synthesize", json={"text": "Hi.", "voice": "nobody"})

    assert response.status_code == 400
    assert "nobody" in response.json()["detail"]


def test_synthesize_returns_400_when_nothing_is_left_to_speak():
    set_tts(FakeTextToSpeech())

    response = TestClient(create_app()).post("/v1/speech/synthesize", json={"text": " [1] ** "})

    assert response.status_code == 400


def test_synthesize_rejects_text_over_the_limit():
    set_tts(FakeTextToSpeech())

    response = TestClient(create_app()).post("/v1/speech/synthesize", json={"text": "a" * 5001})

    assert response.status_code == 422


def test_synthesize_returns_503_when_speech_is_not_configured():
    response = TestClient(create_app()).post("/v1/speech/synthesize", json={"text": "Hello."})

    assert response.status_code == 503
    assert "TALK_TO_RAG_TTS" in response.json()["detail"]


def test_spoken_text_keeps_lines_but_drops_markers():
    assert spoken_text("# Summary\n\n- one [1]\n`code`  here") == "Summary\n- one\ncode here"


def test_split_sentences_splits_on_sentence_ends_and_lines():
    assert split_sentences("First one. Second? Third!\nFourth") == ["First one.", "Second?", "Third!", "Fourth"]


def test_split_sentences_bounds_a_long_sentence():
    sentence = ", ".join(["word " * 20] * 10)

    pieces = split_sentences(sentence, max_chars=150)

    assert len(pieces) > 1
    assert all(len(piece) <= 150 for piece in pieces)
    assert " ".join(pieces).split() == sentence.split()


def test_split_sentences_cuts_a_word_longer_than_the_limit():
    assert split_sentences("x" * 25, max_chars=10) == ["x" * 10, "x" * 10, "x" * 5]


def test_token_chunks_cut_after_the_last_space_that_fits():
    space = 0
    ids = [1, 1, space, 2, 2, space, 3, 3, 3]

    assert token_chunks(ids, space, limit=4) == [[1, 1, space], [2, 2, space], [3, 3, 3]]
    assert token_chunks([5] * 9, space, limit=4) == [[5] * 4, [5] * 4, [5]]
    assert token_chunks([], space) == []


def test_unknown_words_are_read_as_known_parts_and_letters():
    spoken = []

    def g2p(text):
        spoken.append(text)
        return "phonemes", []

    assert read_unknown(g2p, frozenset({"vector", "store"}), "pgvector-store2") == ("phonemes", 1)
    assert spoken == ["P G vector store 2"]
