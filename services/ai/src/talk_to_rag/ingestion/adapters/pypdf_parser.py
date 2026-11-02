"""PDF parser adapter using pypdf to extract page-by-page text."""

import io
from pypdf import PdfReader
from pypdf.errors import PyPdfError

from talk_to_rag.ingestion.contracts import IngestionError, ParsedPage


class PyPdfSourceParser:
    """Extracts text page-by-page from PDF files using pypdf."""

    def parse(self, content: bytes) -> list[ParsedPage]:
        if not content:
            raise IngestionError("Document is empty.")

        try:
            reader = PdfReader(io.BytesIO(content))
        except (PyPdfError, Exception) as exc:
            raise IngestionError(f"Corrupt or invalid PDF: {exc}") from exc

        pages: list[ParsedPage] = []
        for index, page in enumerate(reader.pages):
            try:
                text = page.extract_text() or ""
            except Exception as exc:
                raise IngestionError(f"Failed to extract text from page {index + 1}: {exc}") from exc

            cleaned = text.strip()
            if cleaned:
                pages.append(ParsedPage(page_number=index + 1, text=cleaned, section=None))

        if not pages:
            raise IngestionError("No text layer found. This looks like a scanned PDF and needs OCR.")

        return pages
