from talk_to_rag.speech.contracts import SynthesizedSpeech, UnknownVoice
from talk_to_rag.speech.text import split_sentences, wav_bytes

FAKE_SAMPLE_RATE = 8000


class FakeSpeechToText:
    """Returns a fixed transcript. For tests only; select it with TALK_TO_RAG_STT=fake."""

    def __init__(self, transcript: str = "fake transcript") -> None:
        self._transcript = transcript

    def transcribe(self, audio: bytes) -> str:
        return self._transcript


class FakeTextToSpeech:
    """Speaks a tenth of a second of silence per sentence. For tests only; select it with TALK_TO_RAG_TTS=fake."""

    voices = ("fake",)

    def __init__(self) -> None:
        self.spoken: list[str] = []

    def synthesize(self, text: str, voice: str | None = None) -> SynthesizedSpeech:
        if voice is not None and voice not in self.voices:
            raise UnknownVoice(f"Unknown voice '{voice}'.")
        sentences = split_sentences(text)
        self.spoken.extend(sentences)
        frames = len(sentences) * FAKE_SAMPLE_RATE // 10
        return SynthesizedSpeech(wav_bytes(b"\x00\x00" * frames, FAKE_SAMPLE_RATE), frames / FAKE_SAMPLE_RATE)
