"""Contracts for local speech-to-text and text-to-speech."""

from dataclasses import dataclass
from typing import Protocol


class SpeechNotConfigured(RuntimeError):
    """No speech adapter is selected, or its optional dependencies are missing."""


class UnknownVoice(ValueError):
    """The text-to-speech adapter has no voice by that name."""


class SpeechToText(Protocol):
    def transcribe(self, audio: bytes) -> str:
        """Transcribe recorded audio (any container ffmpeg can read, such as webm/opus) to text."""
        ...


@dataclass(frozen=True)
class SynthesizedSpeech:
    wav: bytes
    duration_seconds: float


class TextToSpeech(Protocol):
    def synthesize(self, text: str, voice: str | None = None) -> SynthesizedSpeech:
        """Speak plain text as a mono WAV file. None picks the adapter's default voice."""
        ...
