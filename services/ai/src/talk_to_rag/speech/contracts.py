"""Contracts for local speech-to-text (Whisper) and text-to-speech (Kokoro/Piper)."""

from typing import Protocol, runtime_checkable


@runtime_checkable
class SpeechToText(Protocol):
    def transcribe(self, audio_bytes: bytes) -> str:
        """Transcribe audio bytes to text locally."""
        ...


@runtime_checkable
class TextToSpeech(Protocol):
    def synthesize(self, text: str) -> bytes:
        """Synthesize text into WAV audio bytes locally."""
        ...
