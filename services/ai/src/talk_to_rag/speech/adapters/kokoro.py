import copy
import json
import os
import re

from talk_to_rag.speech.contracts import SpeechNotConfigured, SynthesizedSpeech, UnknownVoice
from talk_to_rag.speech.text import split_sentences, wav_bytes

REPO = "onnx-community/Kokoro-82M-v1.0-ONNX"
REVISION = "1939ad2a8e416c0acfeecc08a694d14ef25f2231"
MODEL_ENV = "TALK_TO_RAG_KOKORO_MODEL"
VOICE_ENV = "TALK_TO_RAG_TTS_VOICE"
DEFAULT_MODEL = "model_quantized"
DEFAULT_VOICE = "af_heart"
SAMPLE_RATE = 24_000
MAX_TOKENS = 510
PAUSE_SECONDS = 0.15
# American (a) and British (b) English, female (f) or male (m): the voices misaki can phonemize.
ENGLISH_VOICE = re.compile(r"[ab][fm]_[a-z]+")


class KokoroTextToSpeech:
    """Kokoro-82M through onnxruntime on CPU, with misaki turning English into phonemes.

    The model and voices download from the Hugging Face Hub on first use. Words misaki does not
    know are read as known word parts and spelled-out letters, never through espeak-ng.
    """

    def __init__(self, model: str | None = None, default_voice: str | None = None) -> None:
        try:
            import numpy as np
            import onnxruntime
            from huggingface_hub import hf_hub_download
            from misaki import en
        except ImportError as error:
            raise SpeechNotConfigured("Kokoro is not installed. Install the 'speech' extra: uv sync --extra speech") from error
        self._np = np
        self._download = lambda filename: hf_hub_download(REPO, filename, revision=REVISION)
        model_file = self._download(f"onnx/{model or os.environ.get(MODEL_ENV, DEFAULT_MODEL)}.onnx")
        self._session = onnxruntime.InferenceSession(model_file, providers=["CPUExecutionProvider"])
        with open(self._download("tokenizer.json"), encoding="utf-8") as file:
            self._vocab: dict[str, int] = json.load(file)["model"]["vocab"]
        self._space = self._vocab[" "]
        self._english = en
        self._g2p_by_accent: dict[bool, object] = {}
        self._voices: dict[str, object] = {}
        self._default_voice = default_voice or os.environ.get(VOICE_ENV, DEFAULT_VOICE)
        self._style(self._default_voice)

    def synthesize(self, text: str, voice: str | None = None) -> SynthesizedSpeech:
        np = self._np
        name = voice or self._default_voice
        style = self._style(name)
        g2p = self._g2p(british=name.startswith("b"))
        pause = np.zeros(int(SAMPLE_RATE * PAUSE_SECONDS), dtype=np.float32)
        parts = []
        for sentence in split_sentences(text):
            phonemes, _tokens = g2p(sentence)
            ids = [self._vocab[symbol] for symbol in phonemes if symbol in self._vocab]
            for chunk in token_chunks(ids, self._space):
                parts.extend([self._infer(chunk, style), pause])
        samples = np.concatenate(parts) if parts else pause
        pcm16 = (np.clip(samples, -1.0, 1.0) * 32767).astype("<i2").tobytes()
        return SynthesizedSpeech(wav_bytes(pcm16, SAMPLE_RATE), len(samples) / SAMPLE_RATE)

    def _infer(self, ids: list[int], style):
        np = self._np
        inputs = {
            "input_ids": np.array([[0, *ids, 0]], dtype=np.int64),
            "style": style[len(ids) - 1],
            "speed": np.array([1.0], dtype=np.float32),
        }
        return self._session.run(None, inputs)[0].ravel().astype(np.float32)

    def _style(self, name: str):
        if name in self._voices:
            return self._voices[name]
        if not ENGLISH_VOICE.fullmatch(name):
            raise UnknownVoice(f"Unknown voice '{name}'. Use an English Kokoro voice such as '{DEFAULT_VOICE}'.")
        from huggingface_hub.errors import EntryNotFoundError

        try:
            path = self._download(f"voices/{name}.bin")
        except EntryNotFoundError as error:
            raise UnknownVoice(f"Unknown voice '{name}'.") from error
        self._voices[name] = self._np.fromfile(path, dtype=self._np.float32).reshape(-1, 1, 256)
        return self._voices[name]

    def _g2p(self, british: bool):
        if british not in self._g2p_by_accent:
            plain = self._english.G2P(trf=False, british=british, fallback=None)
            known = frozenset(plain.lexicon.golds) | frozenset(plain.lexicon.silvers)
            g2p = copy.copy(plain)
            g2p.fallback = lambda token: read_unknown(plain, known, token.text)
            self._g2p_by_accent[british] = g2p
        return self._g2p_by_accent[british]


def token_chunks(ids: list[int], space: int, limit: int = MAX_TOKENS) -> list[list[int]]:
    """Split a sentence the model cannot take in one pass, after the last space that fits."""
    chunks = []
    while len(ids) > limit:
        window = ids[:limit]
        cut = limit - window[::-1].index(space) if space in window else limit
        chunks.append(ids[:cut])
        ids = ids[cut:]
    return [*chunks, ids] if ids else chunks


def read_unknown(g2p, known: frozenset[str], word: str) -> tuple[str, int]:
    """Read "pgvector" as "P G vector": the longest known word parts, letters for the rest."""
    pieces: list[str] = []
    start = 0
    while start < len(word):
        end = next((end for end in range(len(word), start + 2, -1) if word[start:end].lower() in known), None)
        if end is None:
            if word[start].isalnum():
                pieces.append(word[start].upper())
            start += 1
        else:
            pieces.append(word[start:end].lower())
            start = end
    phonemes, _tokens = g2p(" ".join(pieces))
    return phonemes, 1
