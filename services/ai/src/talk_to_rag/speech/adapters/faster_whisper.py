import io
import os

from talk_to_rag.speech.contracts import SpeechNotConfigured

MODEL_ENV = "TALK_TO_RAG_WHISPER_MODEL"
CACHE_ENV = "TALK_TO_RAG_MODEL_CACHE"
DEFAULT_MODEL = "base"


class FasterWhisperSpeechToText:
    """Whisper on CPU through faster-whisper. The model downloads on first use."""

    def __init__(self, model_size: str | None = None) -> None:
        try:
            from faster_whisper import WhisperModel
        except ImportError as error:
            raise SpeechNotConfigured("faster-whisper is not installed. Install the 'speech' extra: uv sync --extra speech") from error
        self._model = WhisperModel(
            model_size or os.environ.get(MODEL_ENV, DEFAULT_MODEL),
            device="cpu",
            compute_type="int8",
            download_root=os.environ.get(CACHE_ENV),
        )

    def transcribe(self, audio: bytes) -> str:
        segments, _info = self._model.transcribe(io.BytesIO(audio), vad_filter=True)
        return " ".join(segment.text.strip() for segment in segments).strip()
