"""Plain text parser adapter."""

from talk_to_rag.ingestion.contracts import IngestionError, ParsedPage


class PlainTextSourceParser:
    """Parses plain text documents into a single page with no section heading."""

    def parse(self, content: bytes) -> list[ParsedPage]:
        if not content:
            raise IngestionError("Document is empty.")

        try:
            text = content.decode("utf-8")
        except UnicodeDecodeError:
            text = content.decode("utf-8", errors="replace")

        cleaned = text.strip()
        if not cleaned:
            raise IngestionError("Document contains no text.")

        return [ParsedPage(page_number=1, text=cleaned, section=None)]
