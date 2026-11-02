"""Contracts for document parsing and chunking during ingestion."""

from collections.abc import Sequence
from dataclasses import dataclass
from typing import Protocol, runtime_checkable


class IngestionError(RuntimeError):
    """Raised when source content cannot be parsed or has no extractable text."""


@dataclass(frozen=True)
class ParsedPage:
    """A page or discrete section of text extracted from a source document."""

    page_number: int
    text: str
    section: str | None = None
    page_label: str | None = None


@dataclass(frozen=True)
class ChunkPayload:
    """A chunk ready to be embedded and stored."""

    page_number: int
    section: str | None
    chunk_index: int
    content: str
    page_label: str | None = None


@runtime_checkable
class SourceParser(Protocol):
    def parse(self, content: bytes) -> list[ParsedPage]:
        """Parse raw document bytes into a sequence of pages."""
        ...


@runtime_checkable
class TextChunker(Protocol):
    def chunk(self, pages: Sequence[ParsedPage]) -> list[ChunkPayload]:
        """Split pages into overlapping chunks that never cross page boundaries."""
        ...
