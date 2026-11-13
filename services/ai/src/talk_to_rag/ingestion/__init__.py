"""Document parsing and chunking for the AI service.

Callers import only from here, never from an adapter.
"""

from talk_to_rag.ingestion.adapters.markdown_parser import MarkdownSourceParser
from talk_to_rag.ingestion.adapters.paragraph_chunker import ParagraphChunker
from talk_to_rag.ingestion.adapters.pdf_parser import PdfSourceParser
from talk_to_rag.ingestion.adapters.text_parser import PlainTextSourceParser
from talk_to_rag.ingestion.contracts import (
    ChunkPayload,
    IngestionError,
    ParsedPage,
    SourceParser,
    TextChunker,
)

_PARSERS = {
    "application/pdf": PdfSourceParser,
    "text/markdown": MarkdownSourceParser,
    "text/x-markdown": MarkdownSourceParser,
    "text/plain": PlainTextSourceParser,
}


def parser_for(content_type: str) -> SourceParser:
    normalized = content_type.lower().split(";")[0].strip()
    parser_cls = _PARSERS.get(normalized)
    if not parser_cls:
        raise IngestionError(f"Unsupported content type: '{content_type}'")
    return parser_cls()


def default_chunker(target_chunk_size: int = 1000, overlap: int = 150) -> TextChunker:
    return ParagraphChunker(target_chunk_size=target_chunk_size, overlap=overlap)


__all__ = [
    "ChunkPayload",
    "IngestionError",
    "ParsedPage",
    "SourceParser",
    "TextChunker",
    "default_chunker",
    "parser_for",
]
