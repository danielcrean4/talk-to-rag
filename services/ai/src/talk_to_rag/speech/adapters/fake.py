import io
import wave
from talk_to_rag.speech.contracts import SpeechToText, TextToSpeech


class FakeSpeechToText:
    def __init__(self, transcript: str = "what is the normal equation"):
        self._transcript = transcript

    def transcribe(self, audio_bytes: bytes) -> str:
        return self._transcript


class FakeTextToSpeech:
    def synthesize(self, text: str) -> bytes:
        buf = io.BytesIO()
        with wave.open(buf, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(16000)
            wav.writeframes(b"\x00" * 16000)
        return buf.getvalue()
