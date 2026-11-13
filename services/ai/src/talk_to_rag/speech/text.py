"""Turn an answer into text worth speaking, in pieces small enough to synthesize one at a time."""

import io
import re
import wave

MAX_SENTENCE_CHARS = 400

_CITATION = re.compile(r"\s*\[\d+(?:\s*,\s*\d+)*\]")
_MARKDOWN = re.compile(r"[*_`#>|]+")
_SPACES = re.compile(r"[ \t]+")
_SENTENCE_END = re.compile(r"(?<=[.!?])\s+|\n+")
_CLAUSE_END = re.compile(r"(?<=[,;:])\s+")


def spoken_text(text: str) -> str:
    """Drop citation markers like [2] and markdown symbols, which should not be read out."""
    text = _CITATION.sub("", text)
    text = _MARKDOWN.sub(" ", text)
    lines = (_SPACES.sub(" ", line).strip() for line in text.splitlines())
    return "\n".join(line for line in lines if line)


def split_sentences(text: str, max_chars: int = MAX_SENTENCE_CHARS) -> list[str]:
    pieces: list[str] = []
    for sentence in _SENTENCE_END.split(text):
        sentence = sentence.strip()
        if sentence:
            pieces.extend(_limit(sentence, max_chars))
    return pieces


def _limit(sentence: str, max_chars: int) -> list[str]:
    if len(sentence) <= max_chars:
        return [sentence]
    pieces: list[str] = []
    current = ""
    for part in _split_long(sentence, max_chars):
        if current and len(current) + 1 + len(part) > max_chars:
            pieces.append(current)
            current = part
        else:
            current = f"{current} {part}" if current else part
    return [*pieces, current] if current else pieces


def _split_long(sentence: str, max_chars: int) -> list[str]:
    parts: list[str] = []
    for clause in _CLAUSE_END.split(sentence):
        if len(clause) <= max_chars:
            parts.append(clause)
            continue
        for word in clause.split():
            parts.extend(word[i : i + max_chars] for i in range(0, len(word), max_chars))
    return parts


def wav_bytes(pcm16: bytes, sample_rate: int) -> bytes:
    """Wrap 16-bit mono PCM samples in a WAV container."""
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(pcm16)
    return buffer.getvalue()
