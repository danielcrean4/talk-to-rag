"""PDF parser adapter using pypdf to extract page-by-page text."""

import io
import re
from pypdf import PasswordType, PdfReader
from pypdf.errors import PyPdfError

from talk_to_rag.ingestion.contracts import IngestionError, ParsedPage


class PyPdfSourceParser:
    """Extracts text page-by-page from PDF files using pypdf."""

    def parse(self, content: bytes) -> list[ParsedPage]:
        if not content:
            raise IngestionError("Document is empty.")

        try:
            reader = PdfReader(io.BytesIO(content))
        except (PyPdfError, ValueError) as exc:
            raise IngestionError(f"Corrupt or invalid PDF: {exc}") from exc

        if reader.is_encrypted and reader.decrypt("") == PasswordType.NOT_DECRYPTED:
            raise IngestionError("PDF is password-protected or encrypted.")

        page_labels = [str(lbl) for lbl in reader.page_labels] if reader.page_labels else []

        pages: list[ParsedPage] = []
        for index, page in enumerate(reader.pages):
            try:
                text = page.extract_text() or ""
            except Exception as exc:
                raise IngestionError(f"Failed to extract text from page {index + 1}: {exc}") from exc

            # Rejoin words hyphenated across line ends ("boot-\nstrapping" -> "bootstrapping")
            unhyphenated = re.sub(r"-\r?\n\s*([a-z])", r"\1", text)
            cleaned = unhyphenated.strip()
            if cleaned:
                label = page_labels[index] if index < len(page_labels) else None
                pages.append(
                    ParsedPage(
                        page_number=index + 1,
                        text=cleaned,
                        section=None,
                        page_label=label,
                    )
                )

        if not pages:
            raise IngestionError("No text layer found. This looks like a scanned PDF and needs OCR.")

        return pages
