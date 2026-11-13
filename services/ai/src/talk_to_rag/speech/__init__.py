"""Speech adapters. Import from here, never from an adapter.

TALK_TO_RAG_STT selects the speech-to-text adapter: "faster-whisper" or "fake".
TALK_TO_RAG_TTS selects the text-to-speech adapter: "kokoro" or "fake".
With nothing selected, get_stt() and get_tts() raise SpeechNotConfigured instead of guessing.
"""

import os

from talk_to_rag.speech.contracts import (
    SpeechNotConfigured,
    SpeechToText,
    SynthesizedSpeech,
    TextToSpeech,
    UnknownVoice,
)

STT_ENV = "TALK_TO_RAG_STT"
TTS_ENV = "TALK_TO_RAG_TTS"

_stt: SpeechToText | None = None
_tts: TextToSpeech | None = None


def get_stt() -> SpeechToText:
    global _stt
    if _stt is not None:
        return _stt
    name = os.environ.get(STT_ENV, "").strip().lower()
    if name == "faster-whisper":
        from talk_to_rag.speech.adapters.faster_whisper import FasterWhisperSpeechToText

        _stt = FasterWhisperSpeechToText()
    elif name == "fake":
        from talk_to_rag.speech.adapters.fake import FakeSpeechToText

        _stt = FakeSpeechToText()
    else:
        raise SpeechNotConfigured(f"Set {STT_ENV} to 'faster-whisper' to enable speech-to-text.")
    return _stt


def set_stt(instance: SpeechToText | None) -> None:
    """Replace the cached adapter. Tests pass a fake, or None to reset."""
    global _stt
    _stt = instance


def get_tts() -> TextToSpeech:
    global _tts
    if _tts is not None:
        return _tts
    name = os.environ.get(TTS_ENV, "").strip().lower()
    if name == "kokoro":
        from talk_to_rag.speech.adapters.kokoro import KokoroTextToSpeech

        _tts = KokoroTextToSpeech()
    elif name == "fake":
        from talk_to_rag.speech.adapters.fake import FakeTextToSpeech

        _tts = FakeTextToSpeech()
    else:
        raise SpeechNotConfigured(f"Set {TTS_ENV} to 'kokoro' to enable text-to-speech.")
    return _tts


def set_tts(instance: TextToSpeech | None) -> None:
    """Replace the cached adapter. Tests pass a fake, or None to reset."""
    global _tts
    _tts = instance


__all__ = [
    "SpeechNotConfigured",
    "SpeechToText",
    "SynthesizedSpeech",
    "TextToSpeech",
    "UnknownVoice",
    "get_stt",
    "get_tts",
    "set_stt",
    "set_tts",
]
