"""Paragraph and sentence boundary text chunker with overlap."""

import re
from collections.abc import Sequence

from talk_to_rag.ingestion.contracts import ChunkPayload, ParsedPage


class ParagraphChunker:
    """Splits pages into overlapping chunks on paragraph and sentence boundaries.

    Chunks never cross page boundaries, preserving exact source locations.
    """

    def __init__(self, target_chunk_size: int = 1000, overlap: int = 150):
        if target_chunk_size <= 0:
            raise ValueError("target_chunk_size must be positive")
        if overlap < 0:
            raise ValueError("overlap must be non-negative")
        if overlap >= target_chunk_size:
            raise ValueError("overlap must be less than target_chunk_size")

        self._target_chunk_size = target_chunk_size
        self._overlap = overlap

    def chunk(self, pages: Sequence[ParsedPage]) -> list[ChunkPayload]:
        chunks: list[ChunkPayload] = []
        chunk_index = 0

        for page in pages:
            page_text = page.text.strip()
            if not page_text:
                continue

            for content in self._chunk_text(page_text):
                chunks.append(
                    ChunkPayload(
                        page_number=page.page_number,
                        page_label=page.page_label,
                        section=page.section,
                        chunk_index=chunk_index,
                        content=content,
                    )
                )
                chunk_index += 1

        return chunks

    def _chunk_text(self, text: str) -> list[str]:
        chunks: list[str] = []
        current_chunk = ""

        for seg in self._split_into_segments(text):
            if not current_chunk:
                current_chunk = seg
            elif len(current_chunk) + 2 + len(seg) <= self._target_chunk_size:
                current_chunk += "\n\n" + seg
            else:
                chunks.append(current_chunk)
                overlap_prefix = self._extract_overlap(current_chunk)
                current_chunk = f"{overlap_prefix}\n\n{seg}" if overlap_prefix else seg

        if current_chunk:
            chunks.append(current_chunk)

        return chunks

    def _extract_overlap(self, text: str) -> str:
        if self._overlap == 0 or len(text) <= self._overlap:
            return ""

        tail = text[-self._overlap :]
        # Snap forward to the next whitespace boundary so we never cut mid-word
        space_idx = tail.find(" ")
        if space_idx != -1 and space_idx < len(tail) - 1:
            return tail[space_idx + 1 :].strip()

        return tail.strip()

    def _split_into_segments(self, text: str) -> list[str]:
        paragraphs = [p.strip() for p in re.split(r"\n\s*\n", text) if p.strip()]
        segments: list[str] = []

        for para in paragraphs:
            if len(para) <= self._target_chunk_size:
                segments.append(para)
            else:
                sentences = [s.strip() for s in re.split(r"(?<=[.!?])\s+", para) if s.strip()]
                for sent in sentences:
                    if len(sent) <= self._target_chunk_size:
                        segments.append(sent)
                    else:
                        segments.extend(self._split_on_words(sent))

        return segments

    def _split_on_words(self, sentence: str) -> list[str]:
        pieces: list[str] = []
        buffer = ""
        for word in sentence.split():
            if not buffer:
                buffer = word
            elif len(buffer) + 1 + len(word) <= self._target_chunk_size:
                buffer += " " + word
            else:
                pieces.append(buffer)
                buffer = word
        if buffer:
            pieces.append(buffer)
        return pieces
