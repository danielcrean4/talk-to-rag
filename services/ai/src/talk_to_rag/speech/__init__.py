import os
from talk_to_rag.speech.adapters.fake import FakeSpeechToText, FakeTextToSpeech
from talk_to_rag.speech.contracts import SpeechToText, TextToSpeech

_stt_instance: SpeechToText | None = None
_tts_instance: TextToSpeech | None = None


def get_stt() -> SpeechToText:
    global _stt_instance
    if _stt_instance is not None:
        return _stt_instance
    return FakeSpeechToText()


def set_stt(instance: SpeechToText | None) -> None:
    global _stt_instance
    _stt_instance = instance


def get_tts() -> TextToSpeech:
    global _tts_instance
    if _tts_instance is not None:
        return _tts_instance
    return FakeTextToSpeech()


def set_tts(instance: TextToSpeech | None) -> None:
    global _tts_instance
    _tts_instance = instance
